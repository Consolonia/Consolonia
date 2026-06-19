using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;

namespace Consolonia.Core.Dummy
{
    // copy of Avalonia.HeadlessServer HeadlessBitmapStub with minor changes
    internal class DummyBitmap : IDrawingContextLayerImpl, IWriteableBitmapImpl
    {
        public DummyBitmap()
            : this(new PixelSize(1, 1), new Vector(1, 1))
        {
        }

        public DummyBitmap(Size size, Vector dpi)
        {
            Size = size;
            Dpi = dpi;
            Size pixel = Size * (Dpi / 96);
            PixelSize = new PixelSize(Math.Max(1, (int)pixel.Width), Math.Max(1, (int)pixel.Height));
            Format = PixelFormat.Bgra8888;
            AlphaFormat = Avalonia.Platform.AlphaFormat.Opaque;
        }

        public DummyBitmap(PixelSize size, Vector dpi)
        {
            PixelSize = size;
            Dpi = dpi;
            Size = PixelSize.ToSizeWithDpi(dpi);
            Format = PixelFormat.Bgra8888;
            AlphaFormat = Avalonia.Platform.AlphaFormat.Opaque;
        }

        public Size Size { get; }

        public void Dispose()
        {
        }

        public IDrawingContextImpl CreateDrawingContext()
        {
            throw new NotImplementedException();
        }

        public bool IsCorrupted => false;

        public void Blit(IDrawingContextImpl context)
        {
        }

        public bool CanBlit => false;

        public Vector Dpi { get; init; }

        public PixelSize PixelSize { get; init; }

        public int Version { get; set; }

        public void Save(string fileName, int? quality = null)
        {
        }

        public void Save(Stream stream, int? quality = null)
        {
        }

        // resharper disable once UnassignedGetOnlyAutoProperty
        public PixelFormat? Format { get; init; }

        // resharper disable once UnassignedGetOnlyAutoProperty
        public AlphaFormat? AlphaFormat { get; init; }


        public ILockedFramebuffer Lock()
        {
            Version++;
            IntPtr mem = Marshal.AllocHGlobal(PixelSize.Width * PixelSize.Height * 4);
            return new LockedFramebuffer(mem, PixelSize, PixelSize.Width * 4, Dpi, Format ?? PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Opaque,
                () => Marshal.FreeHGlobal(mem));
        }
    }
}
