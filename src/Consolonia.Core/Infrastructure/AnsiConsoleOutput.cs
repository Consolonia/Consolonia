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

        private readonly StringBuilder _outputBuffer = new();

        private PixelBufferCoordinate _headBufferPoint;
        private Color _lastBackground = Colors.Transparent;
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

        /// <summary>
        ///     True when the terminal answered that it can report the mouse in pixels (SGR-Pixels,
        ///     DEC private mode 1016). Only detected here: the console reading input turns it on,
        ///     because only it knows whether it can read those reports.
        /// </summary>
        internal bool SupportsSgrPixelsMouse { get; private set; }

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

            if (pixel.Foreground.Symbol.Sixel != null)
            {
                WriteSixel(position, pixel.Foreground.Symbol.Sixel);
                return;
            }

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

            if (pixel.Foreground.Color != _lastForeground || pixel.Background.Color != _lastBackground)
            {
                (object mappedBackground, object mappedForeground) =
                    consoleColorMode.Value.MapColors(pixel.Background.Color, pixel.Foreground.Color,
                        pixel.Foreground.Weight);
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

                if (pixel.Background.Color != _lastBackground)
                {
                    WriteText(Esc.Background(mappedBackground));
                    _lastBackground = pixel.Background.Color;
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
            if (_outputBuffer.Length > 0)
            {
                WaitPauseTaskIfNecessary();

                if (DebugFlushLogPath != null)
                    LogFlushDiagnostics(_outputBuffer);

                // synchronized update (DEC 2026) makes the terminal apply the batch atomically; wrapping here
                // rather than in the render loop keeps begin/end paired even if a frame is abandoned
                bool synchronizedOutput = Capabilities.HasFlag(ConsoleCapabilities.SupportsSynchronizedOutput);
                if (synchronizedOutput)
                    Console.Out.Write(Esc.BeginSynchronizedUpdate);

                // straight from the builder -- no ToString copy of the whole frame
                Console.Out.Write(_outputBuffer);

                if (synchronizedOutput)
                    Console.Out.Write(Esc.EndSynchronizedUpdate);

                // one explicit flush, which with the writer PrepareConsole installed is what turns a
                // frame into a handful of large writes instead of hundreds of small ones
                Console.Out.Flush();
                _outputBuffer.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WriteSixel(PixelBufferCoordinate position, Sixel sixel)
        {
            // RenderTarget calls this outside WritePixel, so it needs the same lock and pause handling as WriteText
            WaitPauseTaskIfNecessary();
            SetCaretPosition(position);

            // sixel payloads are strictly ASCII (data bytes are 0x3F..0x7E), so widening to chars
            // and re-encoding through the UTF-8 writer reproduces the same bytes
            ReadOnlySpan<byte> bytes = sixel.Render();
            char[] chars = ArrayPool<char>.Shared.Rent(bytes.Length);
            try
            {
                int written = Encoding.ASCII.GetChars(bytes, chars);
                _outputBuffer.Append(chars, 0, written);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }

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
            _outputBuffer.Append(str);
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
            Console.SetOut(new StreamWriter(
                Console.OpenStandardOutput(), new UTF8Encoding(false), 65536, true)
            {
                AutoFlush = false
            });

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
            (int cellW, int cellH) = GetConsoleCellSizeHandler?.Invoke() ?? (8, 16);
            CellPixelHeight = cellH;
            CellPixelWidth = cellW;

            // four queries in one round trip: kitty graphics (reply "APC _Gi=31;OK ST", ignored by others),
            // DECRQM modes 2026 and 1016 (reply "CSI?<mode>;<state>$y") and DA1 (reply "ESC[?62;4;22c",
            // feature 4 = sixel). DA1 is answered by every terminal and is the only reply containing 'c',
            // so it fences the read.
            string graphicsProbeResponse = RequestAnsiResponseHandler?.Invoke(
                Esc.QueryKittyGraphicsSupport + Esc.RequestSynchronizedOutputMode +
                Esc.RequestSgrPixelsMouseMode + Esc.RequestDeviceAttributes,
                'c', 1000) ?? string.Empty;
            if (ResponseIndicatesSynchronizedOutputSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsSynchronizedOutput;

            // CONSOLONIA_PIXEL_MOUSE=0 keeps the mouse in whole cells even where pixels are on offer
            SupportsSgrPixelsMouse = ResponseIndicatesSgrPixelsMouseSupport(graphicsProbeResponse) &&
                                     Environment.GetEnvironmentVariable("CONSOLONIA_PIXEL_MOUSE") != "0";
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
            // close any update left open by an interrupted frame, else the terminal withholds output until it times out
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsSynchronizedOutput))
                WriteText(Esc.EndSynchronizedUpdate);

            // free terminal-side image storage held by kitty graphics placements
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics))
                WriteText(Esc.KittyDeleteAllImages);

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
            Flush();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ShowCaret()
        {
            WriteText(Esc.ShowCursor);
            Flush();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ClearScreen()
        {
            WriteText(Esc.ClearScreen);
            _headBufferPoint = new PixelBufferCoordinate(0, 0);
            WriteText(Esc.SetCursorPosition(0, 0));
            Flush();
        }

        private static void LogFlushDiagnostics(StringBuilder frame)
        {
            const string marker = "_Ga=t";
            string text = frame.ToString();
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

            int start = deviceAttributesResponse.IndexOf('?');
            int end = deviceAttributesResponse.LastIndexOf('c');
            if (start < 0 || end <= start)
                return false;

            string[] parameters = deviceAttributesResponse[(start + 1)..end].Split(';');

            // the first parameter is the device class, the rest are supported features
            for (int i = 1; i < parameters.Length; i++)
                if (parameters[i] == "4")
                    return true;

            return false;
        }

        /// <summary>
        ///     Checks whether the response to <see cref="Esc.QueryKittyGraphicsSupport" /> contains the
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
            return ResponseIndicatesPrivateModeSupport(response, 2026);
        }

        /// <summary>
        ///     Checks whether the response to <see cref="Esc.RequestSgrPixelsMouseMode" /> reports DEC
        ///     private mode 1016 as available, by the same DECRPM states as
        ///     <see cref="ResponseIndicatesSynchronizedOutputSupport" />.
        /// </summary>
        internal static bool ResponseIndicatesSgrPixelsMouseSupport(string response)
        {
            return ResponseIndicatesPrivateModeSupport(response, 1016);
        }

        private static bool ResponseIndicatesPrivateModeSupport(string response, int mode)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            string prefix = $"[?{mode};";
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
            if (ch > 0)
                _outputBuffer.Append(ch);
        }
    }
}