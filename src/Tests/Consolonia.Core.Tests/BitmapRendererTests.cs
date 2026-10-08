using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing;
using NUnit.Framework;
using FakeReadableBitmap = Consolonia.Core.Tests.WithLifetimeFixture.SixelBitmapRendererTests.FakeReadableBitmap;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class BitmapRendererTests
    {
        /// <summary>
        ///     The visible part of a picture drawn at its own size is exactly its pixels under that part. Also
        ///     when wrapped for layout, as every bitmap the render interface loads is: the wrapper reports half
        ///     the height, and measuring the picture by it mapped the top half over the whole area.
        /// </summary>
        [TestCase(false, TestName = "VisiblePixelsOfAPictureAtItsOwnSizeAreItsPixelsThere")]
        [TestCase(true, TestName = "VisiblePixelsAreMeasuredByThePixelsNotTheLayoutSize")]
        public void VisiblePixelsAtOwnSize(bool wrappedForLayout)
        {
            var size = new PixelSize(10, 8);
            using var bitmap = new FakeReadableBitmap(size);
            IBitmapImpl source = wrappedForLayout ? new AspectRatioAdjustedBitmap(bitmap) : bitmap;
            var visible = new PixelRect(3, 2, 4, 5);

            byte[] pixels = BitmapRenderer.GetVisiblePixels(source, null, size, visible,
                BitmapInterpolationMode.None);

            byte[] expected = new byte[visible.Width * visible.Height * 4];
            using (ILockedFramebuffer frame = bitmap.Lock())
                for (int row = 0; row < visible.Height; row++)
                    Marshal.Copy(frame.Address + (visible.Y + row) * frame.RowBytes + visible.X * 4, expected,
                        row * visible.Width * 4, visible.Width * 4);
            Assert.That(pixels, Is.EqualTo(expected));
        }
    }
}
