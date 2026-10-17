using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    internal class PixelBufferBitmapImpl(PixelBuffer pixelBuffer) : IBitmapImpl
    {
        public PixelBuffer Buffer { get; } = pixelBuffer;

        public Vector Dpi => new(RenderTarget.AvaloniaHardcodedDpi, RenderTarget.AvaloniaHardcodedDpi);

        public PixelSize PixelSize { get; } = new(pixelBuffer.Width, pixelBuffer.Height);

        public int Version => 1;

        public void Dispose()
        {
        }

        public void Save(Stream stream, BitmapEncoderOptions options)
        {
            throw new NotSupportedException();
        }
    }
}