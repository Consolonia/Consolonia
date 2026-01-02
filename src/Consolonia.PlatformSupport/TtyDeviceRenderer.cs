#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using DynamicData;
using Mono.Unix.Native;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    /// Renders to raw <c>/dev/vcsaN</c> full-screen Linux virtual (TTY) console.
    /// </summary>
    /// <remarks>
    /// raw buffer is 
    /// byte[0] = cols
    /// byte[1]=  rows
    /// byte[2] = cursor x
    /// byte[3] = cursor y  
    /// byte[..] = cell data: {char, attr}, {char, attr}, ...
    /// </remarks>
    internal sealed class TtyDeviceRenderer : IConsoleDeviceRenderer
    {
        private readonly object _sync = new();
        private readonly ConsoleWindowImpl _consoleTopLevelImpl;
        private readonly string _vcsaPath;

        private ConsoleCursor _consoleCursor;
        private int _fd;

        private IntPtr _bufferPtr;
        private int _bufferLength;

        internal TtyDeviceRenderer(ConsoleWindowImpl consoleTopLevelImpl)
        {
            _consoleTopLevelImpl = consoleTopLevelImpl;
            _consoleTopLevelImpl.Resized += OnResized;
            _consoleTopLevelImpl.CursorChanged += OnCursorChanged;

            int ttyNumber = TryGetActiveTtyNumber() ??
                            throw new PlatformNotSupportedException(
                                "Unable to determine active Linux virtual console (tty number) for /dev/vcsaN output.");

            _vcsaPath = ttyNumber == 0 ? "/dev/vcsa" : $"/dev/vcsa{ttyNumber}";

            _fd = Syscall.open(_vcsaPath, OpenFlags.O_WRONLY | OpenFlags.O_CLOEXEC);
            if (_fd < 0)
            {
                Errno errno = Stdlib.GetLastError();
                throw new IOException($"Failed to open '{_vcsaPath}': {errno}");
            }

            AllocBuffer();
        }

        public void Dispose()
        {
            _consoleTopLevelImpl.Resized -= OnResized;
            _consoleTopLevelImpl.CursorChanged -= OnCursorChanged;

            FreeBuffer();

            if (_fd != 0)
            {
                _ = Syscall.close(_fd);
                _fd = 0;
            }
        }

        private void OnCursorChanged(ConsoleCursor consoleCursor)
        {
            if (_consoleCursor.CompareTo(consoleCursor) == 0)
                return;

            _consoleCursor = consoleCursor;
        }

        private void OnResized(Size size, WindowResizeReason reason)
        {
            AllocBuffer();
        }

        private void AllocBuffer()
        {
            lock (_sync)
            {
                byte rows = (byte)Math.Min(byte.MaxValue, _consoleTopLevelImpl.PixelBuffer.Height);
                byte cols = (byte)Math.Min(byte.MaxValue, _consoleTopLevelImpl.PixelBuffer.Width);

                int cellCount = rows * cols;
                int requiredLength = 4 + (cellCount * 2);

                if (requiredLength == _bufferLength && _bufferPtr != IntPtr.Zero)
                    return;

                FreeBuffer();

                _bufferPtr = Marshal.AllocHGlobal(requiredLength);
                _bufferLength = requiredLength;
            }
        }

        private void FreeBuffer()
        {
            lock (_sync)
            {
                if (_bufferPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_bufferPtr);
                    _bufferPtr = IntPtr.Zero;
                    _bufferLength = 0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public unsafe void RenderToDevice()
        {
            IntPtr bufferPtr;
            int bufferLength;

            lock (_sync)
            {
                bufferPtr = _bufferPtr;
                bufferLength = _bufferLength;

                if (bufferPtr == IntPtr.Zero || bufferLength == 0)
                    return;

                PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;

                if (bufferPtr == IntPtr.Zero || bufferLength == 0)
                    return;

                Span<byte> buffer = new Span<byte>((void*)bufferPtr, bufferLength);

                byte rows = (byte)Math.Min(byte.MaxValue, pixelBuffer.Height);
                byte cols = (byte)Math.Min(byte.MaxValue, pixelBuffer.Width);

                buffer[0] = rows;
                buffer[1] = cols;
                buffer[2] = (byte)_consoleCursor.Coordinate.X;
                buffer[3] = (byte)_consoleCursor.Coordinate.Y;

                int iBuffer = 4;
                bool isWide = false;

                for (ushort y = 0; y < rows; y++)
                {
                    isWide = false;
                    for (ushort x = 0; x < cols; x++)
                    {
                        Pixel pixel = pixelBuffer[x, y];

                        // Handle wide glyphs similarly to ConsoleOutputDeviceRenderer.
                        if (pixel.Width > 1)
                        {
                            isWide = true;

                            // If the wide glyph would overlap non-empty continuation cells, render space instead.
                            for (ushort i = 1; i < pixel.Width && x + i < cols; i++)
                            {
                                if (pixelBuffer[(ushort)(x + i), y].Width != 0)
                                {
                                    pixel = Pixel.Space;
                                    break;
                                }
                            }
                        }
                        else if (pixel.Width == 1)
                        {
                            isWide = false;
                        }

                        if (pixel.Width == 0 && !isWide)
                            pixel = Pixel.Space;

                        buffer[iBuffer++] = EncodeChar(pixel);
                        buffer[iBuffer++] = EncodeAttr(pixel);
                    }
                }

                // Single write to the vcsa device.
                // (Kernel may return partial writes, so we loop to ensure the full frame is written.)
                int offset = 0;
                while (offset < bufferLength)
                {
                    int remaining = bufferLength - offset;
                    IntPtr ptr = IntPtr.Add(bufferPtr, offset);

                    long written = Syscall.write(_fd, ptr, (ulong)remaining);
                    if (written < 0)
                    {
                        Errno errno = Stdlib.GetLastError();
                        throw new IOException($"Failed to write to '{_vcsaPath}': {errno}");
                    }

                    offset += (int)written;
                }
            }
        }

        private static int? TryGetActiveTtyNumber()
        {
            // Prefer the controlling terminal for stderr/stdout/stdin; they are most likely to be a TTY.
            // Mono.Posix exposes libc's ttyname.
            string? tty = Syscall.ttyname(2);
            if (string.IsNullOrWhiteSpace(tty)) tty = Syscall.ttyname(1);
            if (string.IsNullOrWhiteSpace(tty)) tty = Syscall.ttyname(0);

            if (string.IsNullOrWhiteSpace(tty))
                return null;

            // Expect /dev/ttyN (Linux VC). If we're on pts/X, we can't render via vcsa.
            const string devTtyPrefix = "/dev/tty";
            if (!tty.StartsWith(devTtyPrefix, StringComparison.Ordinal))
                return null;

            string suffix = tty[devTtyPrefix.Length..];
            if (suffix.Length == 0)
                return 0;

            return int.TryParse(suffix, out int n) ? n : null;
        }


        private static byte EncodeChar(in Pixel pixel)
        {
            // vcsa stores a single byte (code page). We degrade to ASCII when possible.
            char c = pixel.Foreground.Symbol.Character;
            if (c == '\0')
                c = ' ';

            return c <= 0xFF ? (byte)c : (byte)'?';
        }

        private static byte EncodeAttr(in Pixel pixel)
        {
            byte fg = ToVgaNibble(pixel.Foreground.Color);
            byte bg = ToVgaNibble(pixel.Background.Color);
            return (byte)((bg << 4) | (fg & 0x0F));
        }

        private static byte ToVgaNibble(in Avalonia.Media.Color color)
        {
            // Treat transparent as black.
            if (color.A == 0)
                return 0;

            // Map to the nearest of the standard 16 VGA colors.
            return NearestVgaColorIndex(color);
        }

        private static byte NearestVgaColorIndex(in Avalonia.Media.Color c)
        {
            ReadOnlySpan<(byte r, byte g, byte b)> vga =
            [
                (0, 0, 0),
                (0, 0, 170),
                (0, 170, 0),
                (0, 170, 170),
                (170, 0, 0),
                (170, 0, 170),
                (170, 85, 0),
                (170, 170, 170),
                (85, 85, 85),
                (85, 85, 255),
                (85, 255, 85),
                (85, 255, 255),
                (255, 85, 85),
                (255, 85, 255),
                (255, 255, 85),
                (255, 255, 255)
            ];

            int bestIndex = 0;
            int bestDist = int.MaxValue;

            for (int i = 0; i < vga.Length; i++)
            {
                (byte r, byte g, byte b) = vga[i];
                int dr = c.R - r;
                int dg = c.G - g;
                int db = c.B - b;
                int dist = (dr * dr) + (dg * dg) + (db * db);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIndex = i;
                    if (dist == 0)
                        break;
                }
            }

            return (byte)bestIndex;
        }
    }
}