using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing;
using Consolonia.Core.Tests.WithLifetimeFixture;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class BitmapConverterTests
    {
        /// <summary>
        ///     Draws a picture at its own size and checks that the visible pixels are exactly the source pixels
        ///     in the visible rectangle.
        ///     The second case wraps the bitmap in <see cref="AspectRatioAdjustedBitmap" />, as the render
        ///     interface does for every bitmap it loads. The wrapper reports half the height for layout.
        ///     Regression: the converter used that reported size, so the top half was stretched over the whole area.
        /// </summary>
        [TestCase(false, TestName = "VisiblePixelsOfAPictureAtItsOwnSizeAreItsPixelsThere")]
        [TestCase(true, TestName = "VisiblePixelsAreMeasuredByThePixelsNotTheLayoutSize")]
        public void VisiblePixelsAtOwnSize(bool wrappedForLayout)
        {
            var size = new PixelSize(10, 8);
            using var bitmap = new FakeReadableBitmap(size);
            IBitmapImpl source = wrappedForLayout ? new AspectRatioAdjustedBitmap(bitmap) : bitmap;
            var visible = new PixelRect(3, 2, 4, 5);

            byte[] pixels = new byte[visible.Width * visible.Height * 4];
            BitmapConverter.GetVisiblePixels(source, null, size, visible, BitmapInterpolationMode.None, pixels);

            byte[] expected = new byte[visible.Width * visible.Height * 4];
            using (ILockedFramebuffer frame = bitmap.Lock())
            {
                for (int row = 0; row < visible.Height; row++)
                    Marshal.Copy(frame.Address + (visible.Y + row) * frame.RowBytes + visible.X * 4, expected,
                        row * visible.Width * 4, visible.Width * 4);
            }

            Assert.That(pixels, Is.EqualTo(expected));
        }
    }
}