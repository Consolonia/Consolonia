using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     A picture's conversion is kept per size and refilled in place for each version and visible part,
    ///     so an edit allocates no screen-sized buffers.
    /// </summary>
    [TestFixture]
    public class CellConversionTests
    {
        [Test]
        public void TheCellsAreKeptWhileTheirSizeIsUnchanged()
        {
            var conversion = new CellConversion();
            conversion.Prepare(new PixelRect(0, 0, 4, 2));
            PixelBuffer cells = conversion.Cells;
            Assert.That(cells.Width, Is.EqualTo(4));
            Assert.That(cells.Height, Is.EqualTo(2));

            conversion.Prepare(new PixelRect(3, 1, 4, 2));
            Assert.That(conversion.Cells, Is.SameAs(cells), "a scroll keeps the cells");

            conversion.Prepare(new PixelRect(0, 0, 5, 2));
            Assert.That(conversion.Cells, Is.Not.SameAs(cells));
            Assert.That(conversion.Cells.Width, Is.EqualTo(5));
        }

        [Test]
        public void AConversionShowsAVersionOnlyOnceComplete()
        {
            var conversion = new CellConversion();
            var visible = new PixelRect(0, 0, 2, 2);
            Assert.That(conversion.Shows(1, visible), Is.False, "never converted");

            conversion.Prepare(visible);
            Assert.That(conversion.Shows(1, visible), Is.False, "prepared but not converted");

            conversion.Complete(1);
            Assert.That(conversion.Shows(1, visible), Is.True);
            Assert.That(conversion.Shows(2, visible), Is.False, "an edit");
            Assert.That(conversion.Shows(1, new PixelRect(1, 0, 2, 2)), Is.False, "a scroll");

            conversion.Prepare(visible);
            Assert.That(conversion.Shows(1, visible), Is.False,
                "being converted again: a conversion that throws must not leave it claiming the old version");
        }

        [Test]
        public void OneConversionPerSizeTheLeastRecentlyDrawnGoingBeyondFour()
        {
            var conversions = new List<KeyValuePair<ConversionKey, CellConversion>>();
            CellConversion first = CellBitmapConverter<CellConversion>.FindOrAdd(conversions, Key(1));
            Assert.That(CellBitmapConverter<CellConversion>.FindOrAdd(conversions, Key(1)), Is.SameAs(first),
                "the same size is the same conversion, whatever the version");

            for (int size = 2; size <= 4; size++)
                CellBitmapConverter<CellConversion>.FindOrAdd(conversions, Key(size));
            Assert.That(conversions.Count, Is.EqualTo(4));

            CellBitmapConverter<CellConversion>.FindOrAdd(conversions, Key(1)); // drawn again: the most recent now
            CellBitmapConverter<CellConversion>.FindOrAdd(conversions, Key(5));
            Assert.That(conversions.Count, Is.EqualTo(4));
            Assert.That(conversions.ConvertAll(r => r.Key.TargetSize.Width / 8), Is.EqualTo(new[] { 3, 4, 1, 5 }),
                "size 2, the least recently drawn, is gone");
            Assert.That(CellBitmapConverter<CellConversion>.FindOrAdd(conversions, Key(1)), Is.SameAs(first));
        }

        private static ConversionKey Key(int size)
        {
            return new ConversionKey(new PixelSize(size * 8, 16), BitmapInterpolationMode.None);
        }
    }
}