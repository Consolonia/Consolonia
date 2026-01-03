#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor;
using Consolonia.Core.Infrastructure;

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
    internal sealed class TtyDeviceRenderer : IConsoleDeviceRenderer, IDisposable
    {
        // ioctl request code for GIO_UNIMAP (0x4B66)
        private const int GIO_UNIMAP = 0x4B66;

        // File open flags
        private const int O_RDWR = 2;

        // lseek whence values
        private const int SEEK_SET = 0;

        [DllImport("libc", SetLastError = true)]
        private static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern long lseek(int fd, long offset, int whence);

        [DllImport("libc", SetLastError = true)]
        private static extern nint write(int fd, IntPtr buf, nint count);

        private readonly object _sync = new();
        private readonly ConsoleWindowImpl _consoleWindowImpl;
        private readonly string _vcsaPath;

        private ConsoleCursor _consoleCursor;
        private int _fd = -1;
        private GCHandle _bufferHandle;

        private byte[] _screenBuffer;
        private int _bufferLength;
        private IConsoleOutput _consoleOutput;

        internal TtyDeviceRenderer(ConsoleWindowImpl consoleWindowImpl, IConsoleOutput consoleOutput)
        {
            _consoleOutput = consoleOutput;
            _consoleWindowImpl = consoleWindowImpl;
            _consoleWindowImpl.CursorChanged += OnCursorChanged;

            _pixelCache = new Pixel[_consoleWindowImpl.PixelBuffer.Width, _consoleWindowImpl.PixelBuffer.Height];

            // Initialize the Unicode map from the console's font mapping
            InitializeUniMap();

            _vcsaPath = "/dev/vcsa";

            _fd = open(_vcsaPath, O_RDWR);
            if (_fd < 0)
            {
                int errno = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to open {_vcsaPath}: errno {errno}");
            }

            int cellCount = _consoleWindowImpl.PixelBuffer.Height * _consoleWindowImpl.PixelBuffer.Width;
            _bufferLength = (cellCount * 2);
            _screenBuffer = new byte[_bufferLength];

            // Pin the buffer for the lifetime of this object
            _bufferHandle = GCHandle.Alloc(_screenBuffer, GCHandleType.Pinned);
        }

        public void Dispose()
        {
            _consoleWindowImpl.CursorChanged -= OnCursorChanged;

            if (_bufferHandle.IsAllocated)
            {
                _bufferHandle.Free();
            }

            if (_fd >= 0)
            {
                close(_fd);
                _fd = -1;
            }
        }

        private void WriteToDevice(int offset, int length)
        {
            lseek(_fd, 4 + offset, SEEK_SET);
            write(_fd, _bufferHandle.AddrOfPinnedObject() + offset, length);
        }

        private void OnCursorChanged(ConsoleCursor consoleCursor)
        {
            var oldPosition = _consoleCursor.Coordinate;
            var newPosition = consoleCursor.Coordinate;
            _consoleCursor = consoleCursor;

            lock (_sync)
            {
                if (oldPosition.X >= 0 && oldPosition.X < _consoleWindowImpl.PixelBuffer.Width &&
                    oldPosition.Y >= 0 && oldPosition.Y < _consoleWindowImpl.PixelBuffer.Height)
                {
                    // draw old pixel not highlighted
                    _consoleWindowImpl.PixelBuffer.GetPixelForRendering(oldPosition.X, oldPosition.Y, _consoleCursor, out Pixel oldPixel);
                    var iCell = (oldPosition.Y * _consoleWindowImpl.PixelBuffer.Width + oldPosition.X) * 2;
                    _screenBuffer[iCell] = (byte)EncodeChar(oldPixel);
                    _screenBuffer[iCell + 1] = EncodeAttr(oldPixel);
                    WriteToDevice(iCell, 2);
                }

                if (newPosition.X >= 0 && newPosition.X < _consoleWindowImpl.PixelBuffer.Width &&
                    newPosition.Y >= 0 && newPosition.Y < _consoleWindowImpl.PixelBuffer.Height)
                {
                    // draw new pixel highlighted
                    _consoleWindowImpl.PixelBuffer.GetPixelForRendering(newPosition.X, newPosition.Y, _consoleCursor, out Pixel newPixel);
                    var iCell = (newPosition.Y * _consoleWindowImpl.PixelBuffer.Width + newPosition.X) * 2;
                    _screenBuffer[iCell] = (byte)EncodeChar(newPixel);
                    _screenBuffer[iCell + 1] = EncodeAttr(newPixel);
                    WriteToDevice(iCell, 2);
                }
            }
        }


        private Pixel[,] _pixelCache;

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RenderToDevice()
        {
            lock (_sync)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                PixelBuffer pixelBuffer = _consoleWindowImpl.PixelBuffer;

                var rows = pixelBuffer.Height;
                var cols = pixelBuffer.Width;

                PixelBufferCoordinate? caretPosition = null;
                CaretStyle? caretStyle = null;
                int iBuffer = 0;
                for (ushort y = 0; y < rows; y++)
                {
                    for (ushort x = 0; x < cols; x++)
                    {
                        var oldPixel = _pixelCache[x, y];
                        var pixel = pixelBuffer[x, y];
                        if (!pixel.Equals(oldPixel))
                        {
                            if (!_consoleCursor.IsEmpty() &&
                                _consoleCursor.Coordinate.Y == y &&
                                _consoleCursor.Coordinate.X <= x && x < _consoleCursor.Coordinate.X + _consoleCursor.Width)
                            {
                                if (_consoleCursor.Type == " " && pixel.Width == 1)
                                {
                                    // floating cursor tracking effect 
                                    // if we are drawing a " " and the pixel underneath is not wide char
                                    // then we lift the character from the underlying pixel and invert it
                                    char cursorChar = pixel.Foreground.Symbol.Character != '\0'
                                        ? pixel.Foreground.Symbol.Character
                                        : ' ';
                                    pixel = new Pixel(new PixelForeground(new Symbol(cursorChar, 1), pixel.Background.Color),
                                        new PixelBackground(pixel.Background.Color.GetContrastColor()));
                                }
                                else
                                {
                                    char cursorChar = _consoleCursor.Type[x - _consoleCursor.Coordinate.X];
                                    // simply draw the mouse cursor character in the current pixel colors.
                                    Color foreground = pixel.Foreground.Color != Colors.Transparent
                                        ? pixel.Foreground.Color
                                        : pixel.Background.Color.GetContrastColor();
                                    pixel = new Pixel(
                                        new PixelForeground(new Symbol(cursorChar, 1), foreground,
                                            pixel.Foreground.Weight, pixel.Foreground.Style, pixel.Foreground.TextDecoration),
                                        pixel.Background, pixel.CaretStyle);
                                }
                            }


                            // Handle wide glyphs similarly to ConsoleOutputDeviceRenderer.
                            bool isWide = false;
                            if (pixel.Width > 1)
                            {
                                isWide = true;

                                // If the wide glyph would overlap non-empty continuation cells, render space instead.
                                for (ushort i = 1; i < pixel.Width && x + i < pixelBuffer.Width; i++)
                                {
                                    if (pixelBuffer[(ushort)(x + i), y].Width != 0)
                                    {
                                        pixel = Pixel.Space;
                                        break;
                                    }
                                }
                            }

                            if (pixel.Width == 0 && !isWide)
                                pixel = Pixel.Space;

                            if (pixel.IsCaret())
                            {
                                if (caretPosition != null)
                                    throw new InvalidOperationException("Caret is already shown");
                                caretPosition = new PixelBufferCoordinate(x, y);
                                caretStyle = pixel.CaretStyle;
                            }

                            _pixelCache[x, y] = pixel;
                            //Console.WriteLine($"'{pixel.Foreground.Symbol.GetText()}' FG: {pixel.Foreground.Color} BG: {pixel.Background.Color} => {attr}");
                            _screenBuffer[iBuffer++] = EncodeChar(pixel);
                            _screenBuffer[iBuffer++] = EncodeAttr(pixel);
                        }
                        else
                        {
                            iBuffer += 2;
                        }
                    }
                }
                sw.Stop();
                var swWrite = new Stopwatch();
                swWrite.Start();
                // Write entire buffer to device (past 4-byte header)
                WriteToDevice(0, _bufferLength);
                swWrite.Stop();

                var message = $"RenderToBufferDevice time:{sw.ElapsedMilliseconds}    ms RenderToDevice Write time:{swWrite.ElapsedMilliseconds}   ";
                 iBuffer=0;
                for(int i=0;i<message.Length;i++, iBuffer++)
                    _screenBuffer[iBuffer++] = EncodeChar(message[i]);
                WriteToDevice(0, message.Length*2);

                if (caretPosition != null && caretStyle != CaretStyle.None)
                {
                    _consoleOutput.SetCaretPosition((PixelBufferCoordinate)caretPosition);
                    _consoleOutput.SetCaretStyle((CaretStyle)caretStyle!);
                    _consoleOutput.ShowCaret();
                }
                else
                {
                    _consoleOutput.HideCaret(); //todo: Caret was hidden at the beginning of this method, why to hide it again?
                }
            }
        }


        private byte EncodeChar(in Pixel pixel)
        {
            return EncodeChar(pixel.Foreground.Symbol.Character);
        }

        private byte EncodeChar(in Char c)
        {
            // Handle null/empty as space
            if (c == '\0')
                return (byte)' ';

            // Check Unicode to TTY mapping first
            if (UnicodeToTtyCharMap.TryGetValue(c, out byte ttyChar))
                return ttyChar;

            // Unknown character - return '?'
            return (byte)'?';
        }

        private byte EncodeAttr(in Pixel pixel)
        {
            var (background, foreground) = EgaConsoleColorMode.Instance.Value.MapColors(pixel.Background.Color, pixel.Foreground.Color, pixel.Foreground.Weight);

            return (byte)(((int)background << 4) | ((int)foreground & 0x0F));
        }

        /// <summary>
        /// Represents a Unicode to font position mapping pair.
        /// Used with the GIO_UNIMAP ioctl.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct UniPair
        {
            public ushort Unicode;
            public ushort FontPos;
        }

        /// <summary>
        /// Descriptor for the Unicode map used with GIO_UNIMAP ioctl.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct UniMapDesc
        {
            public ushort EntryCount;
            public IntPtr Entries;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, uint request, ref UniMapDesc unimapdesc);

        /// <summary>
        /// Unicode to VGA/TTY character mapping (Code Page 437 compatible).
        /// Maps Unicode box-drawing and special characters to their VGA console equivalents.
        /// </summary>
        /// <remarks>
        /// The Linux virtual console uses a character set similar to Code Page 437.
        /// See: https://en.wikipedia.org/wiki/Code_page_437
        /// This dictionary is populated at runtime using the GIO_UNIMAP ioctl.
        /// </remarks>
        private static readonly Dictionary<char, byte> UnicodeToTtyCharMap = new();

        private static bool _uniMapInitialized = false;
        private static readonly object _uniMapLock = new();

        /// <summary>
        /// Initializes the Unicode to TTY character map by reading the console's
        /// Unicode mapping table via the GIO_UNIMAP ioctl.
        /// </summary>
        private static void InitializeUniMap()
        {
            if (_uniMapInitialized)
                return;

            lock (_uniMapLock)
            {
                if (_uniMapInitialized)
                    return;

                // Open the console device for ioctl
                int fd = open("/dev/tty", O_RDWR);
                if (fd < 0)
                {
                    _uniMapInitialized = true;
                    return;
                }

                try
                {
                    // First, query the number of entries by passing 0 entries
                    // The ioctl will return -1 with ENOMEM (12) but set EntryCount to the required size
                    var desc = new UniMapDesc
                    {
                        EntryCount = 0,
                        Entries = IntPtr.Zero
                    };

                    ioctl(fd, GIO_UNIMAP, ref desc);
                    int requiredEntries = desc.EntryCount;
                    // If the count query didn't give us a count, we can't proceed
                    if (requiredEntries == 0)
                    {
                        _uniMapInitialized = true;
                        return;
                    }

                    var entries = new UniPair[requiredEntries];

                    // Pin the array to get a stable pointer for the ioctl call
                    var handle = GCHandle.Alloc(entries, GCHandleType.Pinned);
                    try
                    {
                        desc = new UniMapDesc
                        {
                            EntryCount = (ushort)requiredEntries,
                            Entries = handle.AddrOfPinnedObject()
                        };

                        int result = ioctl(fd, GIO_UNIMAP, ref desc);
                        if (result == 0)
                        {
                            // Successfully got the unimap
                            for (int i = 0; i < desc.EntryCount; i++)
                            {
                                char unicode = (char)entries[i].Unicode;
                                byte fontPos = (byte)entries[i].FontPos;
                                UnicodeToTtyCharMap[unicode] = fontPos;
                            }
                        }
                    }
                    finally
                    {
                        handle.Free();
                    }
                }
                finally
                {
                    close(fd);
                }

                _uniMapInitialized = true;
            }
        }
    }
}