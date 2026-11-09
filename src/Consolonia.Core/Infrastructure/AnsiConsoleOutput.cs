using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Logging;
using Avalonia.Media;
using Consolonia.Controls;
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

        /// <summary>The maximum number of image payload bytes converted to chars for one write to Console.Out.</summary>
        private const int ImageChunkChars = 4096;

        private static readonly Lazy<IConsoleColorMode> ConsoleColorMode =
            new(() => AvaloniaLocator.Current.GetRequiredService<IConsoleColorMode>());

        // Flush logs the size of each frame at Verbose level in the Consolonia.Output area. To see it, use:
        // .LogToTrace(LogEventLevel.Verbose, LogExtensions.GetAreaName(LogCategory.Output))
        private static readonly string OutputLogArea = LogExtensions.GetAreaName(LogCategory.Output);

        private static readonly UTF8Encoding Utf8NoBom = new(false);

        /// <summary>
        ///     True when the terminal's cursor position is unknown, for example after <see cref="WriteBytes" />.
        ///     The next write then moves the cursor, even if <see cref="_headBufferPoint" /> already matches.
        /// </summary>
        private bool _caretUnknown;

        // Counts for the current frame. Flush logs them and resets them.
        private int _frameChars;
        private int _frameImageChars;
        private int _frameImages;

        private PixelBufferCoordinate _headBufferPoint;

        /// <summary>
        ///     The encoder that draws the images of image cells, using the image protocol the terminal supports.
        ///     The <see cref="IConsoleImageEncoderFactory" /> creates it. Null when there is no factory or the
        ///     factory returned no encoder.
        /// </summary>
        private IConsoleImageEncoder _imageEncoder;

        /// <summary>
        ///     The factory passed to <see cref="RedirectOutput" />. When set, it is used instead of the factory
        ///     registered with <see cref="AvaloniaLocator" />. Otherwise null.
        /// </summary>
        private IConsoleImageEncoderFactory _imageEncoderFactory;

        private Color _lastBackground = Colors.Transparent;
        private bool _lastBackgroundIsDefault;
        private Color _lastForeground = Colors.Transparent;
        private FontStyle? _lastStyle;
        private TextDecorationLocation? _lastTextDecoration;
        private FontWeight? _lastWeight;

        /// <summary>What Console.Out was before <see cref="PrepareConsole" /> replaced it.</summary>
        private TextWriter _originalOut;

        /// <summary>
        ///     True after a character was written in the last column. <see cref="_headBufferPoint" /> then points
        ///     to the start of the next row, where the next character goes. But the terminal's cursor stays in
        ///     the last column with a pending wrap. Only printing a character moves it to the next row. So
        ///     anything else written at the cursor (an image, or the caret) needs an explicit cursor move.
        /// </summary>
        private bool _pendingWrap;

        /// <summary>
        ///     True when the current frame started a synchronized update (DEC mode 2026).
        ///     <see cref="EndUpdate" /> closes it.
        /// </summary>
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
            if (!_caretUnknown && !_pendingWrap && bufferPoint.Equals(_headBufferPoint)) return;

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

            if (pixel.Background.Image != null && _imageEncoder != null)
            {
                WriteTextOverPicture(position, in pixel);
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
        ///     Writes escape sequences given as ASCII bytes, mainly image payloads, without first building a string.
        /// </summary>
        /// <remarks>
        ///     The payload may move the cursor, so the cursor position is unknown afterwards. The next write
        ///     moves the cursor explicitly.
        /// </remarks>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WriteBytes(ReadOnlySpan<byte> ascii)
        {
            WaitPauseTaskIfNecessary();

            _caretUnknown = true;
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
        ///     Starts a synchronized update (DEC mode 2026) if the terminal supports it and none is open.
        ///     The terminal then shows everything written until <see cref="EndUpdate" /> at once. The writer
        ///     may still send parts of a large frame to the terminal before that.
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
        }

        /// <summary>
        ///     Ends the frame. The image encoder updates its images to match <paramref name="screen" />, and the
        ///     cursor moves back to the caret position. Then the synchronized update is closed and the output is
        ///     flushed. The close and flush happen even if the encoder throws.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void EndUpdate(PixelBuffer screen)
        {
            try
            {
                if (_imageEncoder != null)
                {
                    PixelBufferCoordinate caret = _headBufferPoint;
                    _imageEncoder.WriteImages(screen);
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
        ///     Clears the cached terminal state, because another program may have changed it. This includes the
        ///     last colors and text attributes, the cursor position, and the images the image encoder has placed.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ResetCache()
        {
            // Make the next cell write move the cursor, even if _headBufferPoint already matches.
            _caretUnknown = true;
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
            // The writer buffers output, so flush the emoji before reading the cursor position.
            Console.Out.Flush();
            (int left2, _) = Console.GetCursorPosition();
            if (left2 - left == 2)
                Capabilities |= ConsoleCapabilities.SupportsComplexEmoji;

            // Use 8x16 pixels when there is no handler to get the cell size.
            (int cellW, int cellH) = GetConsoleCellSizeHandler?.Invoke() ??
                                     (DefaultCellPixelSize.Width, DefaultCellPixelSize.Height);
            CellPixelHeight = cellH;
            CellPixelWidth = cellW;

            // Send three queries in one round trip:
            // - Kitty graphics query. Kitty-capable terminals reply "APC _Gi=31;OK ST". Others ignore it.
            // - DECRQM for mode 2026 (synchronized output). The reply is "CSI ? 2026;<state>$y".
            // - DA1 (device attributes). The reply looks like "ESC[?62;4;22c". Feature 4 means sixel.
            // Every terminal answers DA1, and it is the only reply that contains 'c'. So the read stops at 'c'.
            string graphicsProbeResponse = RequestAnsiResponseHandler?.Invoke(
                Esc.QueryKittyGraphics + Esc.RequestSynchronizedOutputMode + Esc.RequestDeviceAttributes,
                'c', 1000) ?? string.Empty;
            if (ResponseIndicatesSynchronizedOutputSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsSynchronizedOutput;
            if (DeviceAttributesIndicateSixelSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsSixel;

            // Some terminals send the kitty reply after the DA1 reply. If it is missing, do one more short read.
            if (!ResponseIndicatesKittyGraphicsSupport(graphicsProbeResponse))
                graphicsProbeResponse += RequestAnsiResponseHandler?.Invoke(string.Empty, '\\', 250) ?? string.Empty;
            if (ResponseIndicatesKittyGraphicsSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsKittyGraphics;

            // CONSOLONIA_GRAPHICS can force a protocol on a terminal that supports it but does not answer the
            // query. It can also turn the image protocols off.
            Capabilities = ApplyGraphicsProtocolOverride(Capabilities,
                Environment.GetEnvironmentVariable("CONSOLONIA_GRAPHICS"));
            InstallImageEncoder();

            BlackColorTTYWorkaround();

            ClearScreen();
#pragma warning restore CA1303 // Do not pass literals as localized parameters
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RestoreConsole()
        {
            // Close a synchronized update that is still open (for example after an interruption).
            // Otherwise the terminal would hold back all output that follows.
            CloseUpdate();

            // Let the encoder free the data it stored in the terminal (such as uploaded images) while we
            // still own the terminal. Then forget what it placed, because none of it is on screen anymore.
            _imageEncoder?.RestoreConsole();
            _imageEncoder?.Reset();

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
            SetCaretPositionInternal(new PixelBufferCoordinate(0, 0));
            Flush();
        }

        /// <summary>
        ///     Creates the image encoder again from the <see cref="IConsoleImageEncoderFactory" /> registered with
        ///     <see cref="AvaloniaLocator" /> (<c>UseConsoloniaImaging</c> registers one). The console is
        ///     prepared before the application is set up, so the factory is not registered yet at that time.
        ///     Call this method after the factory is registered.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void RefreshImageEncoder()
        {
            InstallImageEncoder();
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
        ///     Writes the text of a cell that has an image background. The encoder draws the image itself.
        ///     If the protocol draws images below the text (<see cref="IConsoleImageEncoder.SupportsTextOverlay" />),
        ///     the cell is written with the terminal's default background. Every terminal shows the image through
        ///     the default background. Some terminals hide it under any explicit background, even black.
        ///     If the image replaces the cells it covers, a space is not written, so the image stays visible.
        ///     A glyph is written as a normal text cell on the cell's effective background color.
        /// </summary>
        private void WriteTextOverPicture(PixelBufferCoordinate position, in Pixel pixel)
        {
            if (_imageEncoder.SupportsTextOverlay)
            {
                WriteTextCell(position, in pixel, true);
                return;
            }

            // Writing a space here would erase the image, so skip it.
            if (pixel.Foreground.Symbol.IsWhiteSpace)
                return;

            // The protocol cannot draw text over the image, and the cell has a glyph. Write the glyph on the
            // cell's effective background color, which approximates the image.
            var textCell = new Pixel(pixel.Foreground, new PixelBackground(pixel.EffectiveBackgroundColor),
                pixel.CaretStyle);
            WriteTextCell(position, in textCell, false);
        }

        /// <summary>
        ///     Writes one cell as a glyph with its colors. If <paramref name="defaultBackground" /> is true, the
        ///     cell uses the terminal's default background instead of its own background color. This is for a
        ///     cell over an image that the protocol draws below the text. Some terminals hide such an image under
        ///     any explicit cell background.
        /// </summary>
        private void WriteTextCell(PixelBufferCoordinate position, in Pixel pixel, bool defaultBackground)
        {
            //todo: performance of retrieval of the service, at least can be retrieved once
            Lazy<IConsoleColorMode> consoleColorMode = ConsoleColorMode;

            // A pending wrap needs no cursor move. Printing the character wraps the cursor to the next row,
            // which is where _headBufferPoint already points.
            if (_caretUnknown || !position.Equals(_headBufferPoint))
                SetCaretPositionInternal(position);

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
                // With defaultBackground, the mapped background is not used (the default background is written
                // instead), so black is only a placeholder here.
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
                _pendingWrap = position.X >= Size.Width;
                if (_pendingWrap) position = new PixelBufferCoordinate(0, (ushort)(position.Y + 1));

                _headBufferPoint = position;
            }
        }

        /// <summary>
        ///     Asks the factory for an encoder for the image protocol in <see cref="Capabilities" />. If the
        ///     factory returns one, the console supports pixel images
        ///     (<see cref="ConsoleCapabilities.SupportsPixelImages" />). Otherwise images are drawn with block
        ///     characters.
        /// </summary>
        private void InstallImageEncoder()
        {
            IConsoleImageEncoderFactory factory =
                _imageEncoderFactory ?? AvaloniaLocator.Current.GetService<IConsoleImageEncoderFactory>();
            _imageEncoder = factory?.CreateEncoder(this);
            if (_imageEncoder != null)
                Capabilities |= ConsoleCapabilities.SupportsPixelImages;
            else
                Capabilities &= ~ConsoleCapabilities.SupportsPixelImages;
        }

        /// <summary>
        ///     Parses a Primary Device Attributes (DA1) response such as "ESC[?62;4;22c".
        ///     Feature parameter 4 (following the device class) indicates sixel graphics support.
        /// </summary>
        internal static bool DeviceAttributesIndicateSixelSupport(string deviceAttributesResponse)
        {
            if (string.IsNullOrEmpty(deviceAttributesResponse))
                return false;

            // The probe's other replies (DECRPM "ESC[?2026;2$y" and the kitty APC reply) come first.
            // So find the DA1 reply by its final 'c', then find the "[?" that starts it.
            int end = deviceAttributesResponse.LastIndexOf('c');
            int start = end > 0 ? deviceAttributesResponse.LastIndexOf("[?", end, StringComparison.Ordinal) : -1;
            if (start < 0)
                return false;

            string[] parameters = deviceAttributesResponse[(start + 2)..end].Split(';');

            // The first parameter is the device class. The rest are supported features.
            for (int i = 1; i < parameters.Length; i++)
                if (parameters[i] == "4")
                    return true;

            return false;
        }

        /// <summary>
        ///     Checks whether the response to <see cref="Esc.QueryKittyGraphics" /> contains the
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
        ///     Applies the CONSOLONIA_GRAPHICS environment variable to the detected capabilities.
        ///     "kitty" turns kitty graphics on. "sixel" turns sixel on and kitty off. "quad" turns both
        ///     image protocols off. Any other value keeps the detected capabilities. The value is not case-sensitive.
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
        ///     Replaces Console.Out with a writer over <paramref name="stream" /> that has a large buffer and
        ///     does not flush automatically (see <see cref="PrepareConsole" />).
        /// </summary>
        private static void InstallWriter(Stream stream)
        {
            Console.SetOut(new StreamWriter(stream, Utf8NoBom, 65536, true)
            {
                AutoFlush = false
            });
        }

        /// <summary>
        ///     Sends output to <paramref name="stream" /> instead of stdout, without the terminal setup that
        ///     <see cref="PrepareConsole" /> does. Uses the given <paramref name="capabilities" /> and an encoder
        ///     from <paramref name="imageEncoderFactory" />. Used by tests and performance measurements.
        /// </summary>
        internal void RedirectOutput(Stream stream, ConsoleCapabilities capabilities = ConsoleCapabilities.None,
            IConsoleImageEncoderFactory imageEncoderFactory = null)
        {
            _imageEncoderFactory = imageEncoderFactory;
            InstallWriter(stream);
            Capabilities = capabilities;
            CellPixelWidth = DefaultCellPixelSize.Width;
            CellPixelHeight = DefaultCellPixelSize.Height;
            InstallImageEncoder();
        }

        private void SetCaretPositionInternal(PixelBufferCoordinate bufferPoint)
        {
            WriteText(Esc.SetCursorPosition(bufferPoint.X, bufferPoint.Y));
            _headBufferPoint = bufferPoint;
            _caretUnknown = false;
            _pendingWrap = false;
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