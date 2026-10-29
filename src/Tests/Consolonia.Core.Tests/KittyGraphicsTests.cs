using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class KittyGraphicsTests
    {
        private static readonly string Apc = (char)27 + "_G";
        private static readonly string St = (char)27 + @"\";

        /// <summary>
        ///     Test helper: the transmit sequence <see cref="KittyGraphics.WriteTransmitSequence" /> writes,
        ///     composed by the same code, as text.
        /// </summary>
        internal static string TransmitSequence(int imageId, int pixelWidth, int pixelHeight, byte[] data,
            KittyImageFormat format, bool zlibCompressed = false)
        {
            byte[] sequence = new byte[KittyGraphics.MaxTransmitLength(data.Length)];
            int length = KittyGraphics.ComposeTransmit(sequence, imageId, pixelWidth, pixelHeight, data, format,
                zlibCompressed);
            return Encoding.ASCII.GetString(sequence, 0, length);
        }

        [Test]
        public void AllocatedImageIdsArePositive()
        {
            int imageId = KittyGraphics.AllocateImageId();
            Assert.That(imageId, Is.GreaterThan(0));
        }

        [Test]
        public void AllocatedIdsStayPositiveAfterTheCounterOverflows()
        {
            FieldInfo counter = typeof(KittyGraphics).GetField("_nextImageId",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(counter, Is.Not.Null, "_nextImageId has been renamed; this test needs updating");
            object saved = counter!.GetValue(null);
            try
            {
                counter.SetValue(null, int.MaxValue - 1);
                for (int i = 0; i < 4; i++)
                {
                    int imageId = KittyGraphics.AllocateImageId();
                    Assert.That(imageId, Is.InRange(1, int.MaxValue), "an id after the counter wrapped");
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

        /// <summary>
        ///     Every image transmitted is in the terminal until deleted, and at exit or when the terminal's
        ///     contents are forgotten they are all deleted by id: deleting the visible placements alone
        ///     (d=A) would leave the ones kept for showing again.
        /// </summary>
        [Test]
        public void TransmittedImagesAreDeletedTogetherUnlessDeletedOneByOne()
        {
            KittyGraphics.BuildDeleteTransmittedImagesSequence(); // whatever other tests left in
            int kept = KittyGraphics.AllocateImageId();
            int deleted = KittyGraphics.AllocateImageId();
            KittyGraphics.BuildDeleteSequence(deleted);

            Assert.That(KittyGraphics.BuildDeleteTransmittedImagesSequence(),
                Is.EqualTo(Apc + "a=d,d=I,q=2,i=" + kept.ToString(CultureInfo.InvariantCulture) + St),
                "the one still in the terminal");
            Assert.That(KittyGraphics.BuildDeleteTransmittedImagesSequence(), Is.Empty, "all counted as gone");
        }

        [Test]
        public void SmallImageIsTransmittedInSingleChunk()
        {
            byte[] rgba = { 1, 2, 3, 4 };

            string sequence = TransmitSequence(3, 1, 1, rgba, KittyImageFormat.Rgba);

            Assert.That(sequence,
                Is.EqualTo(Apc + "a=t,f=32,q=2,i=3,s=1,v=1,m=0;" + Convert.ToBase64String(rgba) + St));
        }

        [Test]
        public void PngImageIsTransmittedAsFormat100WithoutDimensions()
        {
            // a PNG payload carries its own dimensions, so no s= and v= keys
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

            string sequence = TransmitSequence(4, 100, 200, png, KittyImageFormat.Png);

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

            string sequence = TransmitSequence(9, 50, 40, rgba, KittyImageFormat.Rgba);

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
        }
    }
}