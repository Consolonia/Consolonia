#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        private readonly object _sync = new();
        private readonly ConsoleWindowImpl _consoleWindowImpl;
        private readonly string _vcsaPath;

        private ConsoleCursor _consoleCursor;
        private FileStream _fs;

        private byte[] _screenBuffer;
        private int _bufferLength;
        private IConsoleOutput _consoleOutput;

        internal TtyDeviceRenderer(ConsoleWindowImpl consoleWindowImpl, IConsoleOutput consoleOutput)
        {
            _consoleOutput = consoleOutput;
            _consoleWindowImpl = consoleWindowImpl;
            _consoleWindowImpl.CursorChanged += OnCursorChanged;

            // Initialize the Unicode map from the console's font mapping
            InitializeUniMap();

            _vcsaPath = "/dev/vcsa";

            _fs = new(_vcsaPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            int cellCount = _consoleWindowImpl.PixelBuffer.Height * _consoleWindowImpl.PixelBuffer.Width;
            _bufferLength = (cellCount * 2);
            _screenBuffer = new byte[_bufferLength];
        }

        public void Dispose()
        {
            _consoleWindowImpl.CursorChanged -= OnCursorChanged;

            if (_fs != null)
            {
                _fs.Dispose();
                _fs = null;
            }
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
                    var oldPixel = _consoleWindowImpl.PixelBuffer.GetPixelForRendering(oldPosition.X, oldPosition.Y, _consoleCursor);
                    var iCell = (oldPosition.Y * _consoleWindowImpl.PixelBuffer.Width + oldPosition.X) * 2;
                    _screenBuffer[iCell] = (byte)EncodeChar(oldPixel);
                    _screenBuffer[iCell + 1] = EncodeAttr(oldPixel);
                     _fs.Seek(4 + iCell, SeekOrigin.Begin);
                     _fs.Write(_screenBuffer.AsSpan(iCell, 2));
                }

                if (newPosition.X >= 0 && newPosition.X < _consoleWindowImpl.PixelBuffer.Width &&
                    newPosition.Y >= 0 && newPosition.Y < _consoleWindowImpl.PixelBuffer.Height)
                {
                    // draw new pixel highlighted
                    var newPixel = _consoleWindowImpl.PixelBuffer.GetPixelForRendering(newPosition.X, newPosition.Y, _consoleCursor);
                    var iCell = (newPosition.Y * _consoleWindowImpl.PixelBuffer.Width + newPosition.X) * 2;
                    _screenBuffer[iCell] = (byte)EncodeChar(newPixel);
                    _screenBuffer[iCell + 1] = EncodeAttr(newPixel);
                     _fs.Seek(4 + iCell, SeekOrigin.Begin);
                     _fs.Write(_screenBuffer.AsSpan(iCell, 2));
                }
                 _fs.Flush(true);
            }
        }
        private int counter = 0;
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
                        Pixel pixel = pixelBuffer.GetPixelForRendering(x, y, _consoleCursor);
                        if (pixel.IsCaret())
                        {
                            if (caretPosition != null)
                                throw new InvalidOperationException("Caret is already shown");
                            caretPosition = new PixelBufferCoordinate(x, y);
                            caretStyle = pixel.CaretStyle;
                        }

                        //Console.WriteLine($"'{pixel.Foreground.Symbol.GetText()}' FG: {pixel.Foreground.Color} BG: {pixel.Background.Color} => {attr}");
                        _screenBuffer[iBuffer++] = EncodeChar(pixel);
                        _screenBuffer[iBuffer++] = EncodeAttr(pixel);
                    }
                }

                // seek past header rows/cols to first cell
                _fs.Seek(4, SeekOrigin.Begin);
                _fs.Write(_screenBuffer);
                _fs.Flush(true);
                sw.Stop();
                
                _consoleOutput.Flush();
                _consoleOutput.SetCaretPosition(new PixelBufferCoordinate(0, 0));
                _consoleOutput.WriteText($"RenderToDevice time:{sw.ElapsedMilliseconds} ms\n");
                _consoleOutput.Flush();

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
            char c = pixel.Foreground.Symbol.Character;

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
                using var ttyFs = new FileStream("/dev/tty", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                int fd = (int)ttyFs.SafeFileHandle.DangerousGetHandle();

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
                    else
                    {
                        // ioctl failed; log error
                        int errno = Marshal.GetLastWin32Error();
                        Console.WriteLine($"GIO_UNIMAP ioctl failed with error code: {errno} {result}");
                    }
                }
                finally
                {
                    handle.Free();
                }

                _uniMapInitialized = true;
            }
        }
    }
}