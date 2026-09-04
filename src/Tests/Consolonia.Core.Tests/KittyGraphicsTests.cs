using System;
using System.Text;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class KittyGraphicsTests
    {
        private static readonly string Apc = (char)27 + "_G";
        private static readonly string St = (char)27 + @"\";
        private static readonly string Placeholder = char.ConvertFromUtf32(0x10EEEE);

        [Test]
        public void PlaceholderCharacterIsUnicodePlaceholderCodepoint()
        {
            Assert.That(KittyGraphics.PlaceholderCharacter, Is.EqualTo(Placeholder));
        }

        [Test]
        public void PlaceholderCellEncodesRowAndColumnWithDiacritics()
        {
            // the first three diacritics of kitty's rowcolumn-diacritics table
            string first = char.ConvertFromUtf32(0x305);
            string second = char.ConvertFromUtf32(0x30D);
            string third = char.ConvertFromUtf32(0x30E);

            Assert.That(KittyGraphics.GetPlaceholderCell(0, 0), Is.EqualTo(Placeholder + first + first));
            Assert.That(KittyGraphics.GetPlaceholderCell(1, 2), Is.EqualTo(Placeholder + second + third));
        }

        [Test]
        public void MaxPlacementSizeMatchesDiacriticsTable()
        {
            Assert.That(KittyGraphics.MaxPlacementSize, Is.EqualTo(297));
        }

        [Test]
        public void ImageIdColorCarries24BitId()
        {
            Assert.That(KittyGraphics.GetImageIdColor(0x123456), Is.EqualTo(Color.FromRgb(0x12, 0x34, 0x56)));
        }

        [Test]
        public void AllocatedImageIdsStayWithin24Bits()
        {
            int imageId = KittyGraphics.AllocateImageId();
            Assert.That(imageId, Is.GreaterThan(0));
            Assert.That(imageId, Is.LessThanOrEqualTo(0xFFFFFF));
        }

        [Test]
        public void VirtualPlacementSequenceIsWellFormed()
        {
            Assert.That(KittyGraphics.BuildVirtualPlacementSequence(5, 10, 4),
                Is.EqualTo(Apc + "a=p,U=1,q=2,i=5,c=10,r=4" + St));
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

            string sequence = KittyGraphics.BuildTransmitSequence(3, 1, 1, rgba);

            Assert.That(sequence,
                Is.EqualTo(Apc + "a=t,f=32,q=2,i=3,s=1,v=1,m=0;" + Convert.ToBase64String(rgba) + St));
        }

        [Test]
        public void LargeImageIsTransmittedInChunksWhichReassembleToThePayload()
        {
            // 2000 pixels of RGBA make a base64 payload larger than two 4096 char chunks
            byte[] rgba = new byte[2000 * 4];
            for (int i = 0; i < rgba.Length; i++)
                rgba[i] = unchecked((byte)(i * 31));

            string sequence = KittyGraphics.BuildTransmitSequence(9, 50, 40, rgba);

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
        public void VerbatimSymbolKeepsPlaceholderSequenceUntouched()
        {
            string placeholderCell = KittyGraphics.GetPlaceholderCell(2, 3);

            Symbol symbol = Symbol.FromVerbatim(placeholderCell, 1);

            // no variation selector may be appended: the exact codepoint sequence is meaningful to the terminal
            Assert.That(symbol.Complex, Is.EqualTo(placeholderCell));
            Assert.That(symbol.Width, Is.EqualTo(1));
        }

        [Test]
        public void DeletePlacementSequenceIsWellFormedAndKeepsImageData()
        {
            // lowercase d=i deletes the placements only, so the image can be placed again
            // later without retransmitting its pixels
            Assert.That(KittyGraphics.BuildDeletePlacementSequence(7),
                Is.EqualTo(Apc + "a=d,d=i,q=2,i=7" + St));
        }

        [Test]
        public void ImageIdIsExtractedFromPlaceholderPixel()
        {
            Pixel placeholderPixel = CreatePlaceholderPixel(0x123456);

            Assert.That(KittyGraphics.TryGetImageId(in placeholderPixel, out int imageId), Is.True);
            Assert.That(imageId, Is.EqualTo(0x123456));
        }

        [Test]
        public void ImageIdIsNotExtractedFromOrdinaryPixels()
        {
            var spacePixel = new Pixel(new PixelForeground(Symbol.Space, Colors.White),
                new PixelBackground(Colors.Black));
            var emojiPixel = new Pixel(new Symbol("👍"), Colors.White);

            Assert.That(KittyGraphics.TryGetImageId(in spacePixel, out _), Is.False);
            Assert.That(KittyGraphics.TryGetImageId(in emojiPixel, out _), Is.False);
        }

        [Test]
        public void PlacementReclaimIsAOneShot()
        {
            KittyGraphics.MarkPlacementDeleted(123456);

            Assert.That(KittyGraphics.TryReclaimPlacement(123456), Is.True);
            Assert.That(KittyGraphics.TryReclaimPlacement(123456), Is.False);
        }

        [Test]
        public void OverwritingPlaceholderCellWithOpaqueBackgroundErasesThePlaceholder()
        {
            // regression: navigating to another screen paints an opaque background over the image
            // area; the blended cell must not keep the placeholder (or the terminal would keep
            // rendering the image slice) and must compare unequal so the diff repaints it
            Pixel placeholderPixel = CreatePlaceholderPixel(0x123456);

            Pixel overwritten = placeholderPixel.Blend(new Pixel(new PixelBackground(Colors.Black)));

            Assert.That(KittyGraphics.TryGetImageId(in overwritten, out _), Is.False);
            Assert.That(overwritten.Foreground.Symbol.Complex, Is.Null);
            Assert.That(overwritten, Is.Not.EqualTo(placeholderPixel));
        }

        private static Pixel CreatePlaceholderPixel(int imageId)
        {
            // mirrors what KittyBitmapRenderer.TransmitAndCreatePlaceholders puts into the buffer
            return new Pixel(
                new PixelForeground(Symbol.FromVerbatim(KittyGraphics.GetPlaceholderCell(0, 0), 1),
                    KittyGraphics.GetImageIdColor(imageId)),
                PixelBackground.Transparent);
        }
    }
}
