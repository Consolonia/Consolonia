using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    /// <summary>
    ///     Minimal readable bitmap over a pinned BGRA buffer. The real render interface cannot create one
    ///     here because Consolonia.Core.Tests has no Skia fallback.
    /// </summary>
    internal sealed class FakeReadableBitmap : IReadableBitmapImpl
    {
        private readonly byte[] _pixels;

        public FakeReadableBitmap(PixelSize size, byte tint = 128)
        {
            PixelSize = size;
            RowBytes = size.Width * 4;
            _pixels = GC.AllocateArray<byte>(RowBytes * size.Height, true);

            // a recognizable gradient, so every cell differs
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int offset = y * RowBytes + x * 4;
                _pixels[offset] = (byte)(x * 255 / Math.Max(1, size.Width - 1));
                _pixels[offset + 1] = (byte)(y * 255 / Math.Max(1, size.Height - 1));
                _pixels[offset + 2] = tint;
                _pixels[offset + 3] = 255;
            }
        }

        private int RowBytes { get; }

        public Vector Dpi => new(96, 96);

        public PixelSize PixelSize { get; }

        public int Version { get; set; } = 1;

        public PixelFormat? Format => PixelFormat.Bgra8888;

        public AlphaFormat? AlphaFormat => Avalonia.Platform.AlphaFormat.Premul;

        public ILockedFramebuffer Lock()
        {
            return new PinnedFramebuffer(_pixels, PixelSize, RowBytes, Dpi);
        }

        public void Dispose()
        {
        }

        public void Save(Stream stream, BitmapEncoderOptions options)
        {
            throw new NotSupportedException();
        }

        /// <summary>Paints <paramref name="area" /> one opaque color.</summary>
        public FakeReadableBitmap Fill(PixelRect area, byte blue, byte green, byte red)
        {
            for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                int offset = y * RowBytes + x * 4;
                _pixels[offset] = blue;
                _pixels[offset + 1] = green;
                _pixels[offset + 2] = red;
                _pixels[offset + 3] = 255;
            }

            return this;
        }

        private sealed class PinnedFramebuffer : ILockedFramebuffer
        {
            public PinnedFramebuffer(byte[] pinnedPixels, PixelSize size, int rowBytes, Vector dpi)
            {
                Address = Marshal.UnsafeAddrOfPinnedArrayElement(pinnedPixels, 0);
                Size = size;
                RowBytes = rowBytes;
                Dpi = dpi;
            }

            public IntPtr Address { get; }

            public PixelSize Size { get; }

            public int RowBytes { get; }

            public Vector Dpi { get; }

            public PixelFormat Format => PixelFormat.Bgra8888;

            public AlphaFormat AlphaFormat => AlphaFormat.Premul;

            public void Dispose()
            {
            }
        }
    }
}