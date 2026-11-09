using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     A test bitmap backed by a pinned BGRA pixel array. The converter reads it through
    ///     <see cref="Lock" />, the same way it reads a real bitmap.
    /// </summary>
    internal sealed class PinnedBitmap : IReadableBitmapImpl
    {
        public PinnedBitmap(byte[] bgra, int width, int height)
        {
            Pixels = GC.AllocateArray<byte>(bgra.Length, true);
            bgra.CopyTo(Pixels, 0);
            PixelSize = new PixelSize(width, height);
        }

        /// <summary>
        ///     The pixel data. To edit the bitmap, change these bytes in place and then increment
        ///     <see cref="Version" />.
        /// </summary>
        public byte[] Pixels { get; }

        public Vector Dpi => new(96, 96);

        public PixelSize PixelSize { get; }

        public int Version { get; set; } = 1;

        public PixelFormat? Format => PixelFormat.Bgra8888;

        public AlphaFormat? AlphaFormat => Avalonia.Platform.AlphaFormat.Premul;

        public ILockedFramebuffer Lock()
        {
            return new PinnedFramebuffer(Marshal.UnsafeAddrOfPinnedArrayElement(Pixels, 0), PixelSize);
        }

        public void Dispose()
        {
        }

        public void Save(Stream stream, BitmapEncoderOptions options)
        {
            throw new NotSupportedException();
        }

        private sealed class PinnedFramebuffer(IntPtr address, PixelSize size) : ILockedFramebuffer
        {
            public IntPtr Address { get; } = address;

            public PixelSize Size { get; } = size;

            public int RowBytes => Size.Width * 4;

            public Vector Dpi => new(96, 96);

            public PixelFormat Format => PixelFormat.Bgra8888;

            public AlphaFormat AlphaFormat => AlphaFormat.Premul;

            public void Dispose()
            {
            }
        }
    }
}