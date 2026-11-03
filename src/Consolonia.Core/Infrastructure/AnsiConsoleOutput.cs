using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Logging;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers.Logging;
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

        /// <summary>Characters of an image payload widened per write to Console.Out.</summary>
        private const int ImageChunkChars = 4096;

        private static readonly Lazy<IConsoleColorMode> ConsoleColorMode =
            new(() => AvaloniaLocator.Current.GetRequiredService<IConsoleColorMode>());

        // each flushed frame's size, logged at Verbose in the Consolonia.Output area:
        // .LogToTrace(LogEventLevel.Verbose, LogExtensions.GetAreaName(LogCategory.Output))
        private static readonly string OutputLogArea = LogExtensions.GetAreaName(LogCategory.Output);

        private static readonly UTF8Encoding Utf8NoBom = new(false);

        // what the frame being written holds, for the flush log
        private int _frameChars;
        private int _frameImageChars;
        private int _frameImages;

        private PixelBufferCoordinate _headBufferPoint;

        /// <summary>
        ///     Shows the image cells, for the image protocol the console answered the probe with; null when
        ///     it answered none. See <see cref="PrepareConsole" />.
        /// </summary>
        private IConsoleImageEncoder _imageEncoder;

        private Color _lastBackground = Colors.Transparent;
        private bool _lastBackgroundIsDefault;
        private Color _lastForeground = Colors.Transparent;
        private FontStyle? _lastStyle;
        private TextDecorationLocation? _lastTextDecoration;
        private FontWeight? _lastWeight;

        /// <summary>What Console.Out was before <see cref="PrepareConsole" /> replaced it.</summary>
        private TextWriter _originalOut;

        /// <summary>Whether the frame being written opened a synchronized update, which Flush closes.</summary>
        private bool _synchronizedUpdateOpen;

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

            if (pixel.Background.Image != null)
            {
                if (_imageEncoder != null)
                {
                    _imageEncoder.WriteImagePixel(position, in pixel);
                    return;
                }

                // a picture on a console the probe found no image protocol for: block glyphs
                WriteTextCell(position, AsQuadCell(in pixel), false);
                return;
            }

            WriteTextCell(position, in pixel, false);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Flush()
        {
            WaitPauseTaskIfNecessary();
            Console.Out.Flush();

            if (Logger.TryGet(LogEventLevel.Verbose, OutputLogArea) is { } flushLogger)
                flushLogger.Log3("Flushed {Chars} chars and {Images} image payloads of {ImageChars} chars",
                    _frameChars, _frameImages, _frameImageChars);

            _frameChars = 0;
            _frameImages = 0;
            _frameImageChars = 0;
        }

        /// <summary>
        ///     Writes ASCII escape sequences given as bytes, image payloads above all, without building a
        ///     string of them.
        /// </summary>
        /// <remarks>This does not move the caret position, so should only be used for escape commands</remarks>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WriteBytes(ReadOnlySpan<byte> ascii)
        {
            WaitPauseTaskIfNecessary();

            Span<char> chunk = stackalloc char[ImageChunkChars];
            _frameImages++;
            _frameImageChars += ascii.Length;
            while (!ascii.IsEmpty)
            {
                int length = Math.Min(chunk.Length, ascii.Length);
                Encoding.ASCII.GetChars(ascii[..length], chunk);
                Console.Out.Write(chunk[..length]);
                ascii = ascii[length..];
            }
        }

        /// <summary>
        ///     Opens a DEC 2026 synchronized update when the terminal has them: it shows everything up to
        ///     the end marker <see cref="EndUpdate" /> writes at once, though the writer may send parts of a
        ///     large frame before then. The image encoder starts its frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void BeginUpdate()
        {
            if (!_synchronizedUpdateOpen && Capabilities.HasFlag(ConsoleCapabilities.SupportsSynchronizedOutput))
            {
                WaitPauseTaskIfNecessary();
                Console.Out.Write(Esc.BeginSynchronizedUpdate);
                _synchronizedUpdateOpen = true;
            }

            _imageEncoder?.BeginUpdate();
        }

        /// <summary>
        ///     The image encoder places its pictures, the cursor goes back to where the frame left it (the
        ///     caret), and the update is closed and sent, whatever the encoder throws.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void EndUpdate(PixelBuffer screen)
        {
            try
            {
                if (_imageEncoder != null)
                {
                    PixelBufferCoordinate caret = _headBufferPoint;
                    _imageEncoder.EndUpdate(screen);
                    SetCaretPosition(caret);
                }
            }
            finally
            {
                CloseUpdate();
                Flush();
            }
        }

        /// <summary>
        ///     Forgets what the terminal is believed to show: the colors and attributes last set (another
        ///     program may have changed them) and what the image encoder has placed.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ResetCache()
        {
            _lastBackground = Colors.Transparent;
            _lastBackgroundIsDefault = false;
            _lastForeground = Colors.Transparent;
            _lastStyle = null;
            _lastTextDecoration = null;
            _lastWeight = null;
            _imageEncoder?.Reset();
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
            if (string.IsNullOrEmpty(str))
                return;

            _frameChars += str.Length;
            Console.Out.Write(str);
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
            // Done after the encoding change above, because setting OutputEncoding recreates
            // Console.Out and would discard this writer.
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
            // the writer buffers, so flush the emoji before asking where the cursor is
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
            _imageEncoder = CreateImageEncoder();

            BlackColorTTYWorkaround();

            ClearScreen();
#pragma warning restore CA1303 // Do not pass literals as localized parameters
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RestoreConsole()
        {
            // an update an interruption left open would hold back everything that follows
            CloseUpdate();

            // Free the terminal-side storage of every kitty image sent: the ones kept for showing again as
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

        private void CloseUpdate()
        {
            if (!_synchronizedUpdateOpen)
                return;

            WaitPauseTaskIfNecessary();
            Console.Out.Write(Esc.EndSynchronizedUpdate);
            _synchronizedUpdateOpen = false;
        }

        /// <summary>
        ///     An image cell as the text cell a terminal without an image protocol shows for it: a block
        ///     glyph in the picture's colors, under the cell's paint, with the cell's own glyph kept if it
        ///     has one.
        /// </summary>
        private Pixel AsQuadCell(in Pixel pixel)
        {
            CellImage image = pixel.Background.Image;
            Pixel quad = QuadPixelBitmapRenderer.FromPixels(image.Bgra, image.Width, image.Height,
                Capabilities.HasFlag(ConsoleCapabilities.SupportsComplexEmoji));
            Color paint = pixel.Background.Color;
            var painted = new Pixel(
                new PixelForeground(quad.Foreground.Symbol,
                    Pixel.MergeColors(quad.Foreground.Color, paint, true)),
                new PixelBackground(Pixel.MergeColors(quad.Background.Color, paint, false)),
                pixel.CaretStyle);
            // a picture cell with any symbol is written as text; one with none (nothing drawn over the
            // picture) is the picture
            return pixel.Foreground.IsNothingToDraw()
                ? painted
                : new Pixel(pixel.Foreground, painted.Background, pixel.CaretStyle);
        }

        /// <summary>
        ///     Writes a cell as a glyph in colors. With <paramref name="defaultBackground" /> the cell gets the
        ///     terminal's default background rather than its color: for a cell over a kitty placement, which
        ///     Konsole hides under any explicit cell background (see <see cref="KittyConsoleImageEncoder" />).
        /// </summary>
        internal void WriteTextCell(PixelBufferCoordinate position, in Pixel pixel, bool defaultBackground)
        {
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

            bool backgroundChanged = defaultBackground
                ? !_lastBackgroundIsDefault
                : pixel.Background.Color != _lastBackground || _lastBackgroundIsDefault;

            if (pixel.Foreground.Color != _lastForeground || backgroundChanged)
            {
                // an image cell's color is the paint over the picture, not a background the console could map
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

        /// <summary>
        ///     Writes an image's escape sequence at <paramref name="position" />, covering
        ///     <paramref name="cellsWide" /> cells of the row.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void WriteImageBytes(PixelBufferCoordinate position, ReadOnlySpan<byte> bytes, int cellsWide)
        {
            WaitPauseTaskIfNecessary();

            // Always an explicit cursor move. After a glyph in the last column the position is modelled as
            // the start of the next row, but the terminal's cursor is still on the old row with a pending
            // wrap that only the next printable character resolves; an image is not one, so without the
            // move it would land a row up. Eight bytes against a payload of kilobytes.
            SetCaretPositionInternal(position);

            // image payloads are strictly ASCII (sixel data bytes are 0x3F..0x7E)
            WriteBytes(bytes);

            var newPosition = new PixelBufferCoordinate((ushort)(position.X + cellsWide), position.Y);
            SetCaretPositionInternal(newPosition);
        }

        /// <summary>
        ///     The encoder for the image protocol the capabilities name, kitty before sixel, or null when
        ///     they name none: the probe's answer and nothing else.
        /// </summary>
        private IConsoleImageEncoder CreateImageEncoder()
        {
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics))
                return new KittyConsoleImageEncoder(this);

            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsSixel))
                return new SixelConsoleImageEncoder(this);

            return null;
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

        /// <summary>The large-buffered, manually flushed writer over <paramref name="stream" /> (see PrepareConsole).</summary>
        private static void InstallWriter(Stream stream)
        {
            Console.SetOut(new StreamWriter(stream, Utf8NoBom, 65536, true)
            {
                AutoFlush = false
            });
        }

        /// <summary>
        ///     Writes frames to <paramref name="stream" /> the way <see cref="PrepareConsole" /> writes them
        ///     to stdout, without the terminal set-up, for the <paramref name="capabilities" /> given: for
        ///     measuring and testing the output path.
        /// </summary>
        internal void RedirectOutput(Stream stream, ConsoleCapabilities capabilities = ConsoleCapabilities.None)
        {
            InstallWriter(stream);
            Capabilities = capabilities;
            CellPixelWidth = DefaultCellPixelSize.Width;
            CellPixelHeight = DefaultCellPixelSize.Height;
            _imageEncoder = CreateImageEncoder();
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

            _frameChars++;
            Console.Out.Write(ch);
        }
    }
}