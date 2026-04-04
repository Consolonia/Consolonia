using System;
using System.IO;
using Avalonia;
using Avalonia.Platform;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Wraps a bitmap to report aspect-ratio-adjusted dimensions to Avalonia's layout system.
    ///     Console characters are typically 2:1 aspect ratio (twice as tall as wide),
    ///     so we report half the actual height to make layouts work correctly.
    /// </summary>
    internal class AspectRatioAdjustedBitmap : IWriteableBitmapImpl
    {
        public AspectRatioAdjustedBitmap(IBitmapImpl innerBitmap)
        {
            InnerBitmap = innerBitmap ?? throw new ArgumentNullException(nameof(innerBitmap));
        }

        public IBitmapImpl InnerBitmap { get; }

        public Vector Dpi => InnerBitmap.Dpi;

        public PixelSize PixelSize => new(InnerBitmap.PixelSize.Width, InnerBitmap.PixelSize.Height / 2);

        public int Version => InnerBitmap.Version;

        public PixelFormat? Format => (InnerBitmap as IReadableBitmapImpl).Format;

        public AlphaFormat? AlphaFormat => (InnerBitmap as IReadableBitmapWithAlphaImpl).AlphaFormat;

        public void Dispose()
        {
            InnerBitmap.Dispose();
        }

        public void Save(string fileName, int? quality = null)
        {
            InnerBitmap.Save(fileName, quality);
        }

        public void Save(Stream stream, int? quality = null)
        {
            InnerBitmap.Save(stream, quality);
        }

        // IReadableBitmapImpl implementation
        public ILockedFramebuffer Lock()
        {
            if (InnerBitmap is IReadableBitmapImpl readable)
                return readable.Lock();
            throw new NotSupportedException("Inner bitmap does not support reading.");
        }
    }
}