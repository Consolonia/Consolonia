using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Consolonia.Core.Drawing;
using NUnit.Framework;
using SkiaSharp;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     Every bitmap Consolonia hands out must survive being resized, because DrawBitmap resizes
    ///     whatever it is given to the cells it covers.
    /// </summary>
    /// <remarks>
    ///     Against the real Skia renderer, which is what an app gets with UseSkia(): Skia's
    ///     ResizeBitmap accepts only its immutable bitmaps and throws "Invalid source bitmap type"
    ///     for a writeable one. LoadBitmapToWidth/Height used to load writeable ones, so
    ///     Bitmap.DecodeToWidth(...) in an Image crashed the render loop on its first frame.
    /// </remarks>
    [TestFixture]
    public class BitmapResizeTests
    {
        private ConsoloniaRenderInterface _renderInterface;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Skia registers itself in the locator; take it and put back whatever was there, so
            // fixtures that run after this one see the renderer they expect.
            var previous = AvaloniaLocator.Current.GetService<IPlatformRenderInterface>();
            SkiaPlatform.Initialize();
            var skia = AvaloniaLocator.Current.GetService<IPlatformRenderInterface>();
            if (previous != null)
                AvaloniaLocator.CurrentMutable.Bind<IPlatformRenderInterface>().ToConstant(previous);

            _renderInterface = new ConsoloniaRenderInterface(skia);
        }

        private static MemoryStream RedPng(int width, int height)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(SKColors.Red);
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return new MemoryStream(data.ToArray());
        }

        [Test]
        public void ABitmapLoadedToAWidthCanBeResized()
        {
            using IBitmapImpl bitmap = _renderInterface.LoadBitmapToWidth(RedPng(40, 20), 20,
                BitmapInterpolationMode.MediumQuality);

            using IBitmapImpl resized = _renderInterface.ResizeBitmap(bitmap, new PixelSize(8, 8),
                BitmapInterpolationMode.MediumQuality);

            Assert.That(resized, Is.Not.Null);
        }

        [Test]
        public void ABitmapLoadedToAHeightCanBeResized()
        {
            using IBitmapImpl bitmap = _renderInterface.LoadBitmapToHeight(RedPng(40, 20), 10,
                BitmapInterpolationMode.MediumQuality);

            using IBitmapImpl resized = _renderInterface.ResizeBitmap(bitmap, new PixelSize(8, 8),
                BitmapInterpolationMode.MediumQuality);

            Assert.That(resized, Is.Not.Null);
        }

        [Test]
        public void AWriteableBitmapCanBeResizedAndKeepsItsPixels()
        {
            // An app's own WriteableBitmap reaches ResizeBitmap the same way, so it is copied into
            // an immutable bitmap first -- and the copy has to carry the pixels, not just the size.
            const int opaqueBlue = unchecked((int)0xFF0000FF); // BGRA in memory, little-endian
            using IWriteableBitmapImpl writeable = _renderInterface.CreateWriteableBitmap(
                new PixelSize(10, 10), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (ILockedFramebuffer frame = writeable.Lock())
            {
                for (int y = 0; y < frame.Size.Height; y++)
                for (int x = 0; x < frame.Size.Width; x++)
                    Marshal.WriteInt32(frame.Address, y * frame.RowBytes + x * 4, opaqueBlue);
            }

            using IBitmapImpl resized = _renderInterface.ResizeBitmap(writeable, new PixelSize(4, 4),
                BitmapInterpolationMode.MediumQuality);

            using ILockedFramebuffer result = ((IReadableBitmapImpl)resized).Lock();
            int centre = Marshal.ReadInt32(result.Address, 2 * result.RowBytes + 2 * 4);
            Assert.That(centre, Is.EqualTo(opaqueBlue));
        }
    }
}
