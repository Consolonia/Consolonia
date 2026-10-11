using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Text;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Console implementation which uses ANSI escape sequences for output
    /// </summary>
    /// <remarks>
    ///     This console buffers all output and only writes to the console on Flush.
    ///     Thread safe
    /// </remarks>
    public class AnsiConsoleOutput : PauseBase, IConsoleOutput

    {
        private const string TestEmoji = "👨‍👩‍👧‍👦";

        private static readonly Lazy<IConsoleColorMode> ConsoleColorMode =
            new(() => AvaloniaLocator.Current.GetRequiredService<IConsoleColorMode>());

        // set CONSOLONIA_DEBUG_FLUSH to a file path to log each flushed frame's size and kitty transmit (a=t) count
        private static readonly string DebugFlushLogPath =
            Environment.GetEnvironmentVariable("CONSOLONIA_DEBUG_FLUSH");

        private static readonly UTF8Encoding Utf8NoBom = new(false);

        private static readonly byte[] BeginSynchronizedUpdate = Encoding.ASCII.GetBytes(Esc.BeginSynchronizedUpdate);

        private static readonly byte[] EndSynchronizedUpdate = Encoding.ASCII.GetBytes(Esc.EndSynchronizedUpdate);

        /// <summary>Room left at the start of the frame for <see cref="BeginSynchronizedUpdate" />.</summary>
        private static readonly int FramePrefix = BeginSynchronizedUpdate.Length;

        /// <summary>
        ///     A frame buffer grown past this (a full-screen kitty picture is megabytes) goes back to the pool
        ///     after it is written, rather than staying the size of the largest frame ever sent.
        /// </summary>
        private const int KeptFrameBytes = 1024 * 1024;

        private const int InitialFrameBytes = 64 * 1024;

        /// <summary>
        ///     The frame being built, as the UTF-8 bytes the terminal receives, after
        ///     <see cref="FramePrefix" /> bytes of room. Image payloads are ASCII and go in as they are;
        ///     only text is encoded.
        /// </summary>
        private byte[] _frame = ArrayPool<byte>.Shared.Rent(InitialFrameBytes);

        private int _frameLength = FramePrefix;

        private readonly Encoder _encoder = Utf8NoBom.GetEncoder();

        /// <summary>Whether the encoder holds a high surrogate waiting for the low one in the next text.</summary>
        private bool _encoderPending;

        /// <summary>The stream frames are written to as bytes, while Console.Out is still the writer over it.</summary>
        private Stream _stream;

        /// <summary>Console.Out as installed over <see cref="_stream" />: anything else means it was redirected.</summary>
        private TextWriter _installedOut;

        private PixelBufferCoordinate _headBufferPoint;
        private Color _lastBackground = Colors.Transparent;
        private bool _lastBackgroundIsDefault;
        private Color _lastForeground = Colors.Transparent;
        private FontStyle? _lastStyle;
        private TextDecorationLocation? _lastTextDecoration;
        private FontWeight? _lastWeight;

        /// <summary>What Console.Out was before <see cref="PrepareConsole" /> replaced it.</summary>
        private TextWriter _originalOut;

        internal Func<(int CellWidth, int CellHeight)> GetConsoleCellSizeHandler { get; set; }

        internal Func<string, char, int, string> RequestAnsiResponseHandler { get; set; }

        public ConsoleCapabilities Capabilities { get; protected set; }

        public PixelBufferSize Size { get; set; }

        public int CellPixelWidth { get; private set; }

        public int CellPixelHeight { get; private set; }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetTitle(string title)
        {
            WriteText(Esc.SetWindowTitle(title));
            Flush();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetCaretPosition(PixelBufferCoordinate bufferPoint)
        {
            if (bufferPoint.Equals(GetCaretPosition())) return;

            SetCaretPositionInternal(bufferPoint);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public PixelBufferCoordinate GetCaretPosition()
        {
            return _headBufferPoint;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
        {
            if (pixel.Width <=
                0) // todo: do we still need to write width ==0 or -1 ? if so - ensure not to messup the caret position changes 
                return;

            //todo: performance of retrieval of the service, at least can be retrieved once
            Lazy<IConsoleColorMode> consoleColorMode = ConsoleColorMode;

            SetCaretPosition(position);

            if (pixel.Foreground.TextDecoration != _lastTextDecoration)
            {
                // reset previous decoration
                WriteText(_lastTextDecoration switch
                {
                    TextDecorationLocation.Strikethrough => Esc.NoStrikethrough,
                    TextDecorationLocation.Underline => Esc.NoUnderline,
                    _ => string.Empty
                });

                // Add new decoration
                WriteText(pixel.Foreground.TextDecoration switch
                {
                    TextDecorationLocation.Underline => Esc.Underline,
                    TextDecorationLocation.Strikethrough => Esc.Strikethrough,
                    _ => string.Empty
                });
                _lastTextDecoration = pixel.Foreground.TextDecoration;
            }

            FontStyle style = pixel.Foreground.Style ?? FontStyle.Normal;
            if (style != _lastStyle)
            {
                //reset previous style
                WriteText(_lastStyle switch
                {
                    FontStyle.Italic => Esc.NoItalic,
                    _ => string.Empty
                });

                WriteText(style switch
                {
                    FontStyle.Italic => Esc.Italic,
                    _ => string.Empty
                });
                _lastStyle = style;
            }

            FontWeight weight = pixel.Foreground.Weight ?? FontWeight.Normal;
            if (weight != _lastWeight)
            {
                WriteText(weight switch
                {
                    FontWeight.Bold or FontWeight.SemiBold or FontWeight.ExtraBold or FontWeight.Black =>
                        Esc.Bold,
                    FontWeight.Thin or FontWeight.ExtraLight or FontWeight.Light =>
                        Esc.Dim,
                    _ => Esc.Normal
                });
                _lastWeight = weight;
            }

            // A cell showing a kitty image keeps the terminal's default background. Konsole draws
            // below-text (z<0) placements under any explicit cell background, so painting the cell
            // a color -- even black -- hides the picture there; kitty draws them above either way.
            bool defaultBackground = !pixel.Background.Tile.IsEmpty;
            bool backgroundChanged = defaultBackground
                ? !_lastBackgroundIsDefault
                : pixel.Background.Color != _lastBackground || _lastBackgroundIsDefault;

            if (pixel.Foreground.Color != _lastForeground || backgroundChanged)
            {
                // a tile cell's color is a wash, not a background the console could map
                (object mappedBackground, object mappedForeground) =
                    consoleColorMode.Value.MapColors(defaultBackground ? Colors.Black : pixel.Background.Color,
                        pixel.Foreground.Color, pixel.Foreground.Weight);
                if (pixel.Foreground.Color != _lastForeground)
                {
                    if (weight is not FontWeight.Bold
                        and not FontWeight.Black
                        and not FontWeight.SemiBold
                        and not FontWeight.ExtraBold)
                        DarkColorInSomeTerminalsRequiresSwitchToNormalWorkAround(mappedForeground);

                    WriteText(Esc.Foreground(mappedForeground));
                    _lastForeground = pixel.Foreground.Color;
                }

                if (backgroundChanged && defaultBackground)
                {
                    WriteText(Esc.DefaultBackground);
                    _lastBackgroundIsDefault = true;
                }
                else if (backgroundChanged)
                {
                    WriteText(Esc.Background(mappedBackground));
                    _lastBackground = pixel.Background.Color;
                    _lastBackgroundIsDefault = false;
                }
            }

            if (pixel.Width > 1)
            {
                // We write out blank chars because we don't know how many cells will be rendered by the terminal
                // then we draw the complex glyph on top of the blank chars.
                WriteText(new string(' ', pixel.Width));
                SetCaretPositionInternal(position);
            }

            if (pixel.Foreground.Symbol.Complex != null)
                WriteText(pixel.Foreground.Symbol.Complex);
            else
                WriteChar(pixel.Foreground.Symbol.Character);

            position = new PixelBufferCoordinate((ushort)(position.X + pixel.Width), position.Y);
            if (pixel.Width > 1 || pixel.Foreground.Symbol.Complex != null)
                // then we force set the next position to where we want to be because again
                // we can't rely on the terminal to advance the caret correctly.
            {
                SetCaretPositionInternal(position);
            }
            else
            {
                if (position.X >= Size.Width) position = new PixelBufferCoordinate(0, (ushort)(position.Y + 1));

                _headBufferPoint = position;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Flush()
        {
            if (_frameLength == FramePrefix)
                return;

            WaitPauseTaskIfNecessary();
            EndText();

            if (DebugFlushLogPath != null)
                LogFlushDiagnostics(Utf8NoBom.GetString(_frame, FramePrefix, _frameLength - FramePrefix));

            // synchronized update (DEC 2026) makes the terminal apply the batch atomically; wrapping here
            // rather than in the render loop keeps begin/end paired even if a frame is abandoned
            int start = FramePrefix;
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsSynchronizedOutput))
            {
                BeginSynchronizedUpdate.CopyTo(_frame, 0);
                start = 0;
                AppendBytes(EndSynchronizedUpdate);
            }

            if (_stream != null && ReferenceEquals(Console.Out, _installedOut))
            {
                // one write of the whole frame, after whatever was written straight to Console.Out
                Console.Out.Flush();
                _stream.Write(_frame, start, _frameLength - start);
                _stream.Flush();
            }
            else
            {
                // Console.Out was redirected after PrepareConsole (a test, a host capturing output): honour it
                Console.Out.Write(Utf8NoBom.GetString(_frame, start, _frameLength - start));
                Console.Out.Flush();
            }

            _frameLength = FramePrefix;
            if (_frame.Length > KeptFrameBytes)
            {
                ArrayPool<byte>.Shared.Return(_frame);
                _frame = ArrayPool<byte>.Shared.Rent(InitialFrameBytes);
            }
        }

        /// <summary>
        ///     Writes ASCII escape sequences given as bytes, image payloads above all, without converting
        ///     them to text and back.
        /// </summary>
        /// <remarks>This does not move the caret position, so should only be used for escape commands</remarks>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WriteBytes(ReadOnlySpan<byte> ascii)
        {
            WaitPauseTaskIfNecessary();
            AppendBytes(ascii);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WriteSixel(PixelBufferCoordinate position, Sixel sixel)
        {
            // RenderTarget calls this outside WritePixel, so it needs the same lock and pause handling as WriteText
            WaitPauseTaskIfNecessary();

            // Always an explicit cursor move. After a glyph in the last column the position is modelled as
            // the start of the next row, but the terminal's cursor is still on the old row with a pending
            // wrap that only the next printable character resolves; a sixel is not one, so without the
            // move it would land a row up. Eight bytes against a payload of kilobytes.
            SetCaretPositionInternal(position);

            // sixel payloads are strictly ASCII (data bytes are 0x3F..0x7E): the bytes go out as they are
            AppendBytes(sixel.Render());

            var newPosition = new PixelBufferCoordinate((ushort)(position.X + sixel.CellsWidth), position.Y);
            SetCaretPositionInternal(newPosition);
        }

        /// <summary>
        ///     Write raw text to the console
        /// </summary>
        /// <remarks>This does not move the caret position, so should only be used for escape commands</remarks>
        /// <param name="str"></param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WriteText(string str)
        {
            WaitPauseTaskIfNecessary();
            AppendText(str);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void PrepareConsole()
        {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
            Console.OutputEncoding = Encoding.UTF8;

            // Replace Console.Out with a large-buffered, manually flushed writer. The default one
            // carries a 256-character buffer with AutoFlush enabled, so every frame this class so
            // carefully batches into _outputBuffer left the process as hundreds of syscall-sized
            // fragments -- on Windows, each one separately parsed and re-serialized by the
            // pseudoconsole. Measured against a live ConPTY with full-screen frames: ~14 frames a
            // second through the default writer, ~100 through this one flushed once per frame.
            //
            // Installed via SetOut rather than written to directly, so anything that redirects
            // Console.Out afterwards -- a test, a host capturing output -- is honoured exactly as
            // before. Done after the encoding change above, because setting OutputEncoding
            // recreates Console.Out and would discard this writer.
            _originalOut = Console.Out;
            InstallWriter(Console.OpenStandardOutput());

            // enable alternate screen so original console screen is not affected by the app
            Console.Write(Esc.EnableAlternateBuffer);
            Console.Out.Flush();

            Size = new PixelBufferSize((ushort)Console.WindowWidth, (ushort)Console.WindowHeight);

            // Detect complex emoji support by writing a complex emoji and checking cursor position.
            // If the cursor moves 2 positions, it indicates proper rendering of composite surrogate pairs.
            (int left, _) = Console.GetCursorPosition();
            Console.Write(TestEmoji);
            // The writer no longer flushes on its own, and the position read below asks the
            // console -- which cannot have moved the cursor for text it has not received.
            Console.Out.Flush();
            (int left2, _) = Console.GetCursorPosition();
            if (left2 - left == 2)
                Capabilities |= ConsoleCapabilities.SupportsComplexEmoji;

            // 8x16 pixels is the fallback when the terminal does not report its cell size
            (int cellW, int cellH) = GetConsoleCellSizeHandler?.Invoke() ??
                                     (DefaultCellPixelSize.Width, DefaultCellPixelSize.Height);
            CellPixelHeight = cellH;
            CellPixelWidth = cellW;

            // three queries in one round trip: kitty graphics (reply "APC _Gi=31;OK ST", ignored by others),
            // DECRQM mode 2026 (reply "CSI?2026;<state>$y") and DA1 (reply "ESC[?62;4;22c", feature 4 = sixel).
            // DA1 is answered by every terminal and is the only reply containing 'c', so it fences the read.
            string graphicsProbeResponse = RequestAnsiResponseHandler?.Invoke(
                KittyGraphics.QuerySupport + Esc.RequestSynchronizedOutputMode + Esc.RequestDeviceAttributes,
                'c', 1000) ?? string.Empty;
            if (ResponseIndicatesSynchronizedOutputSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsSynchronizedOutput;
            if (DeviceAttributesIndicateSixelSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsSixel;

            // some terminals answer the kitty query asynchronously, after DA1: give it one more short read
            if (!ResponseIndicatesKittyGraphicsSupport(graphicsProbeResponse))
                graphicsProbeResponse += RequestAnsiResponseHandler?.Invoke(string.Empty, '\\', 250) ?? string.Empty;
            if (ResponseIndicatesKittyGraphicsSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsKittyGraphics;

            // override for terminals which render a protocol without answering its query, or to force fallback
            Capabilities = ApplyGraphicsProtocolOverride(Capabilities,
                Environment.GetEnvironmentVariable("CONSOLONIA_GRAPHICS"));

            BlackColorTTYWorkaround();

            ClearScreen();
#pragma warning restore CA1303 // Do not pass literals as localized parameters
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RestoreConsole()
        {
            // close any update left open by an interrupted frame, else the terminal withholds output until it
            // times out. Written straight out: buffered, Flush would wrap it in an update of its own.
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsSynchronizedOutput))
                Console.Out.Write(Esc.EndSynchronizedUpdate);

            // free the terminal-side storage of every kitty image sent: the ones kept for showing again as
            // well as the ones placed, which deleting the visible placements alone would leave behind
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics))
            {
                WriteText(KittyGraphics.BuildDeleteTransmittedImagesSequence());
                WriteText(KittyGraphics.DeleteAllImages);
            }

            WriteText(Esc.DisableAlternateBuffer);
            WriteText(Esc.Reset);
            WriteText(Esc.ShowCursor);
            Flush();

            // The console gets its own writer back, flushed; ours held nothing between flushes.
            if (_originalOut != null)
            {
                Console.SetOut(_originalOut);
                _originalOut = null;
            }

            _stream = null;
            _installedOut = null;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetCaretStyle(CaretStyle caretStyle)
        {
            switch (caretStyle)
            {
                case CaretStyle.BlinkingBlock:
                    WriteText(Esc.BlinkingBlockCursor);
                    break;
                case CaretStyle.SteadyBlock:
                    WriteText(Esc.SteadyBlockCursor);
                    break;
                case CaretStyle.BlinkingUnderline:
                    WriteText(Esc.BlinkingUnderlineCursor);
                    break;
                case CaretStyle.SteadyUnderline:
                    WriteText(Esc.SteadyUnderlineCursor);
                    break;
                case CaretStyle.BlinkingBar:
                    WriteText(Esc.BlinkingBarCursor);
                    break;
                case CaretStyle.SteadyBar:
                    WriteText(Esc.SteadyBarCursor);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(caretStyle), caretStyle, null);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void HideCaret()
        {
            WriteText(Esc.HideCursor);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ShowCaret()
        {
            WriteText(Esc.ShowCursor);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ClearScreen()
        {
            WriteText(Esc.ClearScreen);
            _headBufferPoint = new PixelBufferCoordinate(0, 0);
            WriteText(Esc.SetCursorPosition(0, 0));
            Flush();
        }

        private static void LogFlushDiagnostics(string text)
        {
            const string marker = "_Ga=t";
            int transmits = 0;
            for (int found = text.IndexOf(marker, StringComparison.Ordinal);
                 found >= 0;
                 found = text.IndexOf(marker, found + marker.Length, StringComparison.Ordinal))
                transmits++;

            try
            {
                File.AppendAllText(DebugFlushLogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} chars={text.Length} a=t count={transmits}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // diagnostics must never take the app down
            }
        }

        /// <summary>
        ///     Parses a Primary Device Attributes (DA1) response such as "ESC[?62;4;22c".
        ///     Feature parameter 4 (following the device class) indicates sixel graphics support.
        /// </summary>
        internal static bool DeviceAttributesIndicateSixelSupport(string deviceAttributesResponse)
        {
            if (string.IsNullOrEmpty(deviceAttributesResponse))
                return false;

            // the probe's other replies (DECRPM "ESC[?2026;2$y", kitty's APC) come first, so find the
            // DA1 reply by its final 'c' and take the parameters from its own "[?"
            int end = deviceAttributesResponse.LastIndexOf('c');
            int start = end > 0 ? deviceAttributesResponse.LastIndexOf("[?", end, StringComparison.Ordinal) : -1;
            if (start < 0)
                return false;

            string[] parameters = deviceAttributesResponse[(start + 2)..end].Split(';');

            // the first parameter is the device class, the rest are supported features
            for (int i = 1; i < parameters.Length; i++)
                if (parameters[i] == "4")
                    return true;

            return false;
        }

        /// <summary>
        ///     Checks whether the response to <see cref="KittyGraphics.QuerySupport" /> contains the
        ///     "APC _Gi=31;OK ST" reply a kitty-graphics-capable terminal sends.
        /// </summary>
        internal static bool ResponseIndicatesKittyGraphicsSupport(string response)
        {
            return response != null && response.Contains("_Gi=31;OK", StringComparison.Ordinal);
        }

        /// <summary>
        ///     Checks whether the response to <see cref="Esc.RequestSynchronizedOutputMode" /> reports
        ///     DEC private mode 2026 as available. DECRPM states 1 (set), 2 (reset) and 3 (permanently
        ///     set) mean the terminal applies synchronized updates; 0 (unrecognized) and 4 (permanently
        ///     reset) mean it does not.
        /// </summary>
        internal static bool ResponseIndicatesSynchronizedOutputSupport(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            const string prefix = "[?2026;";
            int start = response.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
                return false;

            int stateStart = start + prefix.Length;
            int end = response.IndexOf("$y", stateStart, StringComparison.Ordinal);
            if (end < 0)
                return false;

            return response[stateStart..end] is "1" or "2" or "3";
        }

        /// <summary>
        ///     Applies the CONSOLONIA_GRAPHICS environment variable override to the detected capabilities:
        ///     "kitty" forces kitty graphics on, "sixel" forces sixel (and kitty off), "quad" disables
        ///     both graphics protocols. Any other value leaves detection untouched.
        /// </summary>
        internal static ConsoleCapabilities ApplyGraphicsProtocolOverride(ConsoleCapabilities capabilities,
            string overrideValue)
        {
            return overrideValue?.Trim().ToUpperInvariant() switch
            {
                "KITTY" => capabilities | ConsoleCapabilities.SupportsKittyGraphics,
                "SIXEL" => (capabilities & ~ConsoleCapabilities.SupportsKittyGraphics) |
                           ConsoleCapabilities.SupportsSixel,
                "QUAD" => capabilities &
                          ~(ConsoleCapabilities.SupportsKittyGraphics | ConsoleCapabilities.SupportsSixel),
                _ => capabilities
            };
        }

        /// <summary>
        ///     In some terminals, dark colors are not displayed correctly when written after bright colors.
        ///     Because bright colors switch terminal state to be bold internally
        /// </summary>
        private void DarkColorInSomeTerminalsRequiresSwitchToNormalWorkAround(object mappedForeground)
        {
            if (mappedForeground is < ConsoleColor.DarkGray)
                WriteText(Esc.Normal);
        }

        /// <summary>
        ///     In TTY
        ///     When the first foreground is black
        ///     We write it black
        ///     But it's gray
        /// </summary>
        private void BlackColorTTYWorkaround()
        {
            const ConsoleColor anotherColor = ConsoleColor.Cyan;

            // Switch to another color makes tty behave correctly after
            WriteText(Esc.Foreground(anotherColor));
            WriteText(Esc.Background(anotherColor));

            // we have to write something, otherwise it does not work
            WriteText(" ");

            // Switching back to black (further we are painting the screen and do other drawings during initialization)
            WriteText(Esc.Foreground(ConsoleColor.Black));
            WriteText(Esc.Background(ConsoleColor.Black));
            Flush();
            //todo: low: we can not simply test the presence of this bug (if it even exists), thus come back to this later
        }

        /// <summary>
        ///     The large-buffered, manually flushed writer over <paramref name="stream" /> (see PrepareConsole),
        ///     and the stream itself for frames, which go to it as bytes in one write each.
        /// </summary>
        private void InstallWriter(Stream stream)
        {
            Console.SetOut(new StreamWriter(stream, Utf8NoBom, 65536, true)
            {
                AutoFlush = false
            });
            _stream = stream;
            // as Console holds it (wrapped for thread safety), to tell a later redirection apart
            _installedOut = Console.Out;
        }

        /// <summary>
        ///     Writes frames to <paramref name="stream" /> the way <see cref="PrepareConsole" /> writes them
        ///     to stdout, without the terminal set-up: for measuring the output path.
        /// </summary>
        internal void RedirectOutput(Stream stream)
        {
            InstallWriter(stream);
        }

        /// <summary>Room for <paramref name="bytes" /> more bytes at the end of the frame.</summary>
        private Span<byte> FrameSpace(int bytes)
        {
            int needed = _frameLength + bytes;
            if (needed > _frame.Length)
            {
                byte[] grown = ArrayPool<byte>.Shared.Rent(Math.Max(needed, _frame.Length * 2));
                _frame.AsSpan(0, _frameLength).CopyTo(grown);
                ArrayPool<byte>.Shared.Return(_frame);
                _frame = grown;
            }

            return _frame.AsSpan(_frameLength);
        }

        /// <summary>Appends <paramref name="text" /> as UTF-8; escape sequences, which are most of it, are ASCII.</summary>
        private void AppendText(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
                return;

            Span<byte> space = FrameSpace(Utf8NoBom.GetMaxByteCount(text.Length));
            int ascii = 0;
            if (!_encoderPending && Ascii.FromUtf16(text, space, out ascii) == OperationStatus.Done)
            {
                _frameLength += ascii;
                return;
            }

            // from the first non-ASCII character on (or all of it, to pair a surrogate held from before)
            _frameLength += ascii + _encoder.GetBytes(text[ascii..], space[ascii..], false);
            _encoderPending = char.IsHighSurrogate(text[^1]);
        }

        /// <summary>Appends ASCII bytes as they are.</summary>
        private void AppendBytes(ReadOnlySpan<byte> bytes)
        {
            EndText();
            bytes.CopyTo(FrameSpace(bytes.Length));
            _frameLength += bytes.Length;
        }

        /// <summary>Writes out a high surrogate the encoder still holds, which no low one followed.</summary>
        private void EndText()
        {
            if (!_encoderPending)
                return;
            _frameLength += _encoder.GetBytes(ReadOnlySpan<char>.Empty, FrameSpace(Utf8NoBom.GetMaxByteCount(0)),
                true);
            _encoderPending = false;
        }

        private void SetCaretPositionInternal(PixelBufferCoordinate bufferPoint)
        {
            WriteText(Esc.SetCursorPosition(bufferPoint.X, bufferPoint.Y));
            _headBufferPoint = bufferPoint;
        }

        /// <summary>
        ///     Write char to the console
        /// </summary>
        /// <param name="ch"></param>
        private void WriteChar(char ch)
        {
            if (ch == 0)
                return;

            if (ch < 0x80 && !_encoderPending)
            {
                FrameSpace(1)[0] = (byte)ch;
                _frameLength++;
                return;
            }

            AppendText(new ReadOnlySpan<char>(in ch));
        }
    }
}