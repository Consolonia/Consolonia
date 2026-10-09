using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class KittyGraphicsTests
    {
        private static readonly string Apc = (char)27 + "_G";
        private static readonly string St = (char)27 + @"\";

        [Test]
        public void AllocatedImageIdsStayWithin24Bits()
        {
            int imageId = KittyGraphics.AllocateImageId();
            Assert.That(imageId, Is.GreaterThan(0));
            Assert.That(imageId, Is.LessThanOrEqualTo(0xFFFFFF));
        }

        [Test]
        public void AllocatedIdsStayWithin24BitsAfterTheCounterOverflows()
        {
            System.Reflection.FieldInfo counter = typeof(KittyGraphics).GetField("_nextImageId",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(counter, Is.Not.Null, "_nextImageId has been renamed; this test needs updating");
            object saved = counter!.GetValue(null);
            try
            {
                counter.SetValue(null, int.MaxValue - 1);
                for (int i = 0; i < 4; i++)
                {
                    int imageId = KittyGraphics.AllocateImageId();
                    Assert.That(imageId, Is.InRange(1, 0xFFFFFF), "an id after the counter wrapped");
                }
            }
            finally
            {
                counter.SetValue(null, saved);
            }
        }

        [Test]
        public void DeleteSequenceIsWellFormed()
        {
            Assert.That(KittyGraphics.BuildDeleteSequence(7),
                Is.EqualTo(Apc + "a=d,d=I,q=2,i=7" + St));
        }

        [Test]
        public void SmallImageIsTransmittedInSingleChunk()
        {
            byte[] rgba = { 1, 2, 3, 4 };

            string sequence = KittyGraphics.BuildTransmitSequence(3, 1, 1, rgba, KittyImageFormat.Rgba);

            Assert.That(sequence,
                Is.EqualTo(Apc + "a=t,f=32,q=2,i=3,s=1,v=1,m=0;" + Convert.ToBase64String(rgba) + St));
        }

        [Test]
        public void PngImageIsTransmittedAsFormat100WithoutDimensions()
        {
            // a PNG payload carries its own dimensions, so no s= and v= keys
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

            string sequence = KittyGraphics.BuildTransmitSequence(4, 100, 200, png, KittyImageFormat.Png);

            Assert.That(sequence,
                Is.EqualTo(Apc + "a=t,f=100,q=2,i=4,m=0;" + Convert.ToBase64String(png) + St));
        }

        [Test]
        public void LargeImageIsTransmittedInChunksWhichReassembleToThePayload()
        {
            // 8000 bytes base64-expand past two 4096 char chunks
            byte[] rgba = new byte[2000 * 4];
            for (int i = 0; i < rgba.Length; i++)
                rgba[i] = unchecked((byte)(i * 31));

            string sequence = KittyGraphics.BuildTransmitSequence(9, 50, 40, rgba, KittyImageFormat.Rgba);

            string[] chunks = sequence.Split(St, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(chunks.Length, Is.GreaterThan(2));

            var payload = new StringBuilder();
            for (int i = 0; i < chunks.Length; i++)
            {
                Assert.That(chunks[i], Does.StartWith(Apc));
                bool last = i == chunks.Length - 1;
                Assert.That(chunks[i], Does.Contain(last ? "m=0;" : "m=1;"));
                Assert.That(chunks[i], last ? Does.Not.Contain("m=1;") : Does.Not.Contain("m=0;"));
                string chunkPayload = chunks[i][(chunks[i].IndexOf(';', StringComparison.Ordinal) + 1)..];
                Assert.That(chunkPayload.Length, Is.LessThanOrEqualTo(4096));
                payload.Append(chunkPayload);
            }

            Assert.That(chunks[0], Does.Contain("a=t,f=32,q=2,i=9,s=50,v=40,"));
            Assert.That(Convert.FromBase64String(payload.ToString()), Is.EqualTo(rgba));
        }

        [Test]
        public void RectPlacementSequencesAreWellFormed()
        {
            Assert.That(KittyGraphics.BuildRectPlacementSequence(5, 7, 16, 32, 64, 48),
                Is.EqualTo("\u001b_Ga=p,q=2,C=1,z=-2,i=5,p=7,x=16,y=32,w=64,h=48\u001b\\"));
            Assert.That(KittyGraphics.BuildDeletePlacementSequence(5, 7),
                Is.EqualTo("\u001b_Ga=d,d=i,q=2,i=5,p=7\u001b\\"));
        }

        [Test]
        public void WashSequencesAreWellFormed()
        {
            // one pixel per cell of a 3x2 screen, zlib compressed raw RGBA
            string transmit = KittyGraphics.BuildTransmitWashSequence(9, Color.FromArgb(0x80, 0x10, 0x20, 0x30), 3, 2);
            const string header = "\u001b_Ga=t,f=32,o=z,q=2,i=9,s=3,v=2,m=0;";
            Assert.That(transmit, Does.StartWith(header));
            Assert.That(transmit, Does.EndWith("\u001b\\"));

            byte[] compressed = Convert.FromBase64String(transmit[header.Length..^2]);
            using var inflated = new MemoryStream();
            using (var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
            {
                zlib.CopyTo(inflated);
            }

            byte[] rgba = inflated.ToArray();
            Assert.That(rgba, Has.Length.EqualTo(3 * 2 * 4));
            for (int i = 0; i < rgba.Length; i += 4)
                Assert.That(rgba[i..(i + 4)], Is.EqualTo(new byte[] { 0x10, 0x20, 0x30, 0x80 }));

            // cropped to one pixel per covered cell, above the image (z=-2) and below text
            Assert.That(KittyGraphics.BuildWashPlacementSequence(9, 11, 40, 12),
                Is.EqualTo("\u001b_Ga=p,q=2,C=1,z=-1,i=9,p=11,x=0,y=0,w=40,h=12,c=40,r=12\u001b\\"));
        }
    }
}