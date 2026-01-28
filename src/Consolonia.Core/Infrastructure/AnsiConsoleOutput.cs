using System;
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
    /// </remarks>
    public class AnsiConsoleOutput : IConsoleOutput

    {
        private const string TestEmoji = "👨‍👩‍👧‍👦";

        private static readonly Lazy<IConsoleColorMode> ConsoleColorMode =
            new(() => AvaloniaLocator.Current.GetRequiredService<IConsoleColorMode>());

        private readonly StringBuilder _outputBuffer = new();

        private PixelBufferCoordinate _headBufferPoint;
        private Color _lastBackground = Colors.Transparent;
        private Color _lastForeground = Colors.Transparent;
        private FontStyle? _lastStyle;
        private TextDecorationLocation? _lastTextDecoration;
        private FontWeight? _lastWeight;

        public ConsoleCapabilities Capabilities { get; protected set; }

        public PixelBufferSize Size { get; set; }

        public void SetTitle(string title)
        {
            WriteText(Esc.SetWindowTitle(title));
            Flush();
        }

        public void SetCaretPosition(PixelBufferCoordinate bufferPoint)
        {
            if (bufferPoint.Equals(GetCaretPosition())) return;

            SetCaretPositionInternal(bufferPoint);
        }

        public PixelBufferCoordinate GetCaretPosition()
        {
            return _headBufferPoint;
        }

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

        public void Flush()
        {
            if (_outputBuffer.Length > 0)
            {
                Console.Write(_outputBuffer.ToString());
                _outputBuffer.Clear();
            }
        }

        /// <summary>
        ///     Write raw text to the console
        /// </summary>
        /// <remarks>This does not move the caret position, so should only be used for escape commands</remarks>
        /// <param name="str"></param>
        public void WriteText(string str)
        {
            _outputBuffer.Append(str);
        }

        public void PrepareConsole()
        {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
            Console.OutputEncoding = Encoding.UTF8;

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

            BlackColorTTYWorkaround();

            ClearScreen();
#pragma warning restore CA1303 // Do not pass literals as localized parameters
        }

        public void RestoreConsole()
        {
            WriteText(Esc.DisableAlternateBuffer);
            WriteText(Esc.Reset);
            WriteText(Esc.ShowCursor);
            Flush();
        }

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

        public void HideCaret()
        {
            WriteText(Esc.HideCursor);
            Flush();
        }

        public void ShowCaret()
        {
            WriteText(Esc.ShowCursor);
            Flush();
        }

        public void ClearScreen()
        {
            WriteText(Esc.ClearScreen);
            _headBufferPoint = new PixelBufferCoordinate(0, 0);
            WriteText(Esc.SetCursorPosition(0, 0));
            Flush();
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
        public void WriteChar(char ch)
        {
            if (ch > 0)
                _outputBuffer.Append(ch);
        }
    }
}