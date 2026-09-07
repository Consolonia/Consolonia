using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls;
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

        private readonly ArrayBufferWriter<byte> _outputBuffer = new();

        // Diagnostic: set CONSOLONIA_DEBUG_FLUSH to a file path to log the byte size of every
        // flushed frame plus how many kitty transmits (a=t) it carried.
        private static readonly string DebugFlushLogPath =
            Environment.GetEnvironmentVariable("CONSOLONIA_DEBUG_FLUSH");

        private PixelBufferCoordinate _headBufferPoint;
        private Color _lastBackground = Colors.Transparent;
        private Color _lastForeground = Colors.Transparent;
        private FontStyle? _lastStyle;
        private TextDecorationLocation? _lastTextDecoration;
        private FontWeight? _lastWeight;
        private Stream _stdOut;

        internal Func<(int CellWidth, int CellHeight)> GetConsoleCellSizeHandler { get; set; }

        internal Func<string, char, int, string> RequestAnsiResponseHandler { get; set; }

        public AnsiConsoleOutput()
        {
            Console.OutputEncoding = Encoding.UTF8;
            _stdOut = Console.OpenStandardOutput();
        }


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
            if (pixel.Width <= 0) // todo: do we still need to write width ==0 or -1 ? if so - ensure not to messup the caret position changes 
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
            if (_outputBuffer.WrittenCount > 0)
            {
                WaitPauseTaskIfNecessary();

                if (DebugFlushLogPath != null)
                    LogFlushDiagnostics(_outputBuffer.WrittenSpan);

                // Wrap every flushed batch in a synchronized update (DEC 2026) where supported, so
                // the terminal applies it atomically instead of repainting mid-parse. Done here
                // rather than by the render loop so begin and end always go out as a pair: a frame
                // abandoned to an exception must not leave the terminal holding output forever.
                bool synchronizedOutput = Capabilities.HasFlag(ConsoleCapabilities.SupportsSynchronizedOutput);
                if (synchronizedOutput)
                    _stdOut.Write(BeginSynchronizedUpdateBytes);
                _stdOut.Write(_outputBuffer.WrittenSpan);
                if (synchronizedOutput)
                    _stdOut.Write(EndSynchronizedUpdateBytes);

                _stdOut.Flush();
                _outputBuffer.Clear();
            }
        }

        private static readonly byte[] BeginSynchronizedUpdateBytes = Encoding.ASCII.GetBytes(Esc.BeginSynchronizedUpdate);
        private static readonly byte[] EndSynchronizedUpdateBytes = Encoding.ASCII.GetBytes(Esc.EndSynchronizedUpdate);

        private static void LogFlushDiagnostics(ReadOnlySpan<byte> frame)
        {
            int transmits = 0;
            ReadOnlySpan<byte> marker = "_Ga=t"u8;
            ReadOnlySpan<byte> rest = frame;
            for (int found = rest.IndexOf(marker); found >= 0; found = rest.IndexOf(marker))
            {
                transmits++;
                rest = rest[(found + marker.Length)..];
            }

            try
            {
                File.AppendAllText(DebugFlushLogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} bytes={frame.Length} a=t count={transmits}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // diagnostics must never take the app down
            }
        }

        public void WriteSixel(PixelBufferCoordinate position, Drawing.Sixel sixel)
        {
            SetCaretPosition(position);

            ReadOnlySpan<byte> bytes = sixel.Render();
            bytes.CopyTo(_outputBuffer.GetSpan(bytes.Length));
            _outputBuffer.Advance(bytes.Length);

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
            int max = Encoding.UTF8.GetMaxByteCount(str.Length);
            Span<byte> span = _outputBuffer.GetSpan(max);
            int written = Encoding.UTF8.GetBytes(str.AsSpan(), span);
            _outputBuffer.Advance(written);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void PrepareConsole()
        {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
            // enable alternate screen so original console screen is not affected by the app
            Console.Write(Esc.EnableAlternateBuffer);

            Size = new PixelBufferSize((ushort)Console.WindowWidth, (ushort)Console.WindowHeight);

            // Detect complex emoji support by writing a complex emoji and checking cursor position.
            // If the cursor moves 2 positions, it indicates proper rendering of composite surrogate pairs.
            (int left, _) = Console.GetCursorPosition();
            Console.Write(TestEmoji);
            (int left2, _) = Console.GetCursorPosition();
            if (left2 - left == 2)
                Capabilities |= ConsoleCapabilities.SupportsComplexEmoji;

            // determine cell pixel sizes
            (int cellW, int cellH) = GetConsoleCellSizeHandler?.Invoke() ?? (8, 16);
            this.CellPixelHeight = cellH;
            this.CellPixelWidth = cellW;

            // Detect terminal graphics and synchronized output support with a single round trip. The
            // kitty graphics query is answered with "APC _Gi=31;OK ST" by supporting terminals and
            // ignored by all others, the DECRQM query for mode 2026 is answered with "CSI?2026;<state>$y"
            // by terminals which know synchronized output, and the Primary Device Attributes (DA1)
            // response, for example ESC[?62;4;22c, reports feature 4 when sixel graphics are supported.
            // DA1 is answered by every terminal, so it also acts as the fence telling us all replies
            // have arrived (neither of the other replies contains a 'c').
            string graphicsProbeResponse = RequestAnsiResponseHandler?.Invoke(
                Esc.QueryKittyGraphicsSupport + Esc.RequestSynchronizedOutputMode + Esc.RequestDeviceAttributes,
                'c', 1000) ?? string.Empty;
            if (ResponseIndicatesSynchronizedOutputSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsSynchronizedOutput;
            if (DeviceAttributesIndicateSixelSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsSixel;

            // Some emulators handle the graphics query asynchronously and reply after DA1,
            // so if the kitty reply was not inside the fenced response give it one more short read.
            if (!ResponseIndicatesKittyGraphicsSupport(graphicsProbeResponse))
                graphicsProbeResponse += RequestAnsiResponseHandler?.Invoke(string.Empty, '\\', 250) ?? string.Empty;
            if (ResponseIndicatesKittyGraphicsSupport(graphicsProbeResponse))
                Capabilities |= ConsoleCapabilities.SupportsKittyGraphics;

            // Allow overriding the detected graphics protocol, for terminals which render a protocol
            // without answering the corresponding query (or to force the fallback for testing).
            Capabilities = ApplyGraphicsProtocolOverride(Capabilities,
                Environment.GetEnvironmentVariable("CONSOLONIA_GRAPHICS"));

            BlackColorTTYWorkaround();

            ClearScreen();
#pragma warning restore CA1303 // Do not pass literals as localized parameters
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RestoreConsole()
        {
            // close any synchronized update left open by an interrupted frame, so the terminal
            // does not sit on withheld output until its fallback timeout expires
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsSynchronizedOutput))
                WriteText(Esc.EndSynchronizedUpdate);

            // free terminal-side image storage held by kitty graphics placements
            if (Capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics))
                WriteText(Esc.KittyDeleteAllImages);

            WriteText(Esc.DisableAlternateBuffer);
            WriteText(Esc.Reset);
            WriteText(Esc.ShowCursor);
            Flush();
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
                // the renderer reads the same variable to pick the kitty mode: rect placements
                // are the default, "kittyplaceholder" selects the legacy unicode-placeholder mode
                "KITTYRECT" => capabilities | ConsoleCapabilities.SupportsKittyGraphics,
                "KITTYPLACEHOLDER" => capabilities | ConsoleCapabilities.SupportsKittyGraphics,
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
            {
                Span<char> chars = stackalloc char[1];
                chars[0] = ch;

                Span<byte> bytes = _outputBuffer.GetSpan(Encoding.UTF8.GetMaxByteCount(1));
                int written = Encoding.UTF8.GetBytes(chars, bytes);
                _outputBuffer.Advance(written);
            }
        }
    }
}