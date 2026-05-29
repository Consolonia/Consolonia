using System;
using System.IO;
using Avalonia;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    internal class PixelBufferBitmapImpl(PixelBuffer pixelBuffer) : IBitmapImpl
    {
        public const double AvaloniaHardcodedDPI = 96;
        public PixelBuffer Buffer { get; } = pixelBuffer;

        public Vector Dpi => new(AvaloniaHardcodedDPI, AvaloniaHardcodedDPI);

        public PixelSize PixelSize { get; } = new(pixelBuffer.Width, pixelBuffer.Height);

        public int Version => 1;

        public void Dispose()
        {
        }

        public void Save(string fileName, int? quality = null)
        {
            throw new NotSupportedException();
        }

        public void Save(Stream stream, int? quality = null)
        {
            throw new NotSupportedException();
        }
    }
}
