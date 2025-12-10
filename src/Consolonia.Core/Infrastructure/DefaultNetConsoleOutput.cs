using System;
using System.Text;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     IConsoleOutput implementation which purely uses Console API and not ANSI escape sequences.
    /// </summary>
    /// <remarks>
    ///     Buffers on TextRuns of shared color/textstyle/properties
    /// </remarks>
    public class DefaultNetConsoleOutput : IConsoleOutput
    {
        private readonly StringBuilder _stringBuilder;
        private PixelBufferCoordinate _currentPosition;
        private Color _lastBackgroundColor;
        private Color _lastForegroundColor;
        private ConsoleColor _originalBackground;
        private ConsoleColor _originalForeground;

        public DefaultNetConsoleOutput()
        {
            _stringBuilder = new StringBuilder();
            _lastBackgroundColor = Colors.Transparent;
            _lastForegroundColor = Colors.Transparent;
        }

        public virtual PixelBufferSize Size { get; set; }

        public ConsoleCapabilities Capabilities { get; protected set; }

        public virtual void SetTitle(string title)
        {
            Console.Title = title;
        }

        public virtual void SetCaretPosition(PixelBufferCoordinate bufferPoint)
        {
            try
            {
                Console.SetCursorPosition(bufferPoint.X, bufferPoint.Y);
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignore
                // this happens while resizing.
            }
        }

        public virtual PixelBufferCoordinate GetCaretPosition()
        {
            (int left, int top) = Console.GetCursorPosition();
            return new PixelBufferCoordinate((ushort)left, (ushort)top);
        }

        public virtual void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
        {
            if (position != _currentPosition)
            {
                Flush();

                SetCaretPosition(position);
                _currentPosition = position;
            }

            if (pixel.Background.Color != _lastBackgroundColor)
            {
                Flush();

                (ConsoleColor consoleColor, _) =
                    EgaConsoleColorMode.Instance.Value.ConvertToConsoleColorMode(pixel.Background.Color, false);
                Console.BackgroundColor = consoleColor;
                _lastBackgroundColor = pixel.Background.Color;
            }

            if (pixel.Foreground.Color != _lastForegroundColor)
            {
                Flush();

                (ConsoleColor consoleColor, _) =
                    EgaConsoleColorMode.Instance.Value.ConvertToConsoleColorMode(pixel.Foreground.Color, true);
                Console.ForegroundColor = consoleColor;
                _lastForegroundColor = pixel.Foreground.Color;
            }

            if (pixel.Foreground.Symbol.Complex != null)
                _stringBuilder.Append(pixel.Foreground.Symbol.Complex);
            else
                _stringBuilder.Append(pixel.Foreground.Symbol.Character);

            _currentPosition = new PixelBufferCoordinate((ushort)(_currentPosition.X + pixel.Width),
                _currentPosition.Y);
        }

        public virtual void Flush()
        {
            if (_stringBuilder.Length == 0)
                return;

            // Debug.WriteLine($"[{_currentBufferPoint.X},{_currentBufferPoint.Y}] {_lastForegroundColor} on {_lastBackgroundColor} '{_stringBuilder}'");
            Console.Write(_stringBuilder.ToString());
            _stringBuilder.Clear();
        }

        public virtual void WriteText(string str)
        {
            Console.Write(str);
        }

        public virtual void SetCaretStyle(CaretStyle caretStyle)
        {
            try
            {
#pragma warning disable CA1416 // Validate platform compatibility
                Console.CursorSize = caretStyle switch
                {
                    CaretStyle.SteadyBlock => 100,
                    CaretStyle.BlinkingBlock => 100,
                    CaretStyle.SteadyUnderline => 1,
                    CaretStyle.BlinkingUnderline => 1,
                    CaretStyle.SteadyBar => 50,
                    CaretStyle.BlinkingBar => 50,
                    _ => throw new ArgumentOutOfRangeException(nameof(caretStyle))
                };
#pragma warning restore CA1416 // Validate platform compatibility
            }
            catch (PlatformNotSupportedException)
            {
                //todo: should be Consolonia exception? Should we avoid custom CursorSize on higher level in Theme for instance?
            }
        }

        public virtual void HideCaret()
        {
            Console.CursorVisible = false;
        }

        public virtual void ShowCaret()
        {
            Console.CursorVisible = true;
        }

        public virtual void PrepareConsole()
        {
            Console.OutputEncoding = Encoding.UTF8;

            _originalForeground = Console.ForegroundColor;
            _originalBackground = Console.BackgroundColor;
            Size = new PixelBufferSize((ushort)Console.WindowWidth, (ushort)Console.WindowHeight);
            // Detect complex emoji support by writing a complex emoji and checking cursor position.
            // If the cursor moves 2 positions, it indicates proper rendering of composite surrogate pairs.
            (int left, _) = Console.GetCursorPosition();
            WriteText("👨‍👩‍👧‍👦");
            (int left2, _) = Console.GetCursorPosition();
            if (left2 - left == 2)
                Capabilities |= ConsoleCapabilities.SupportsComplexEmoji;

            Console.Clear();
        }

        public virtual void RestoreConsole()
        {
            Console.ForegroundColor = _originalForeground;
            Console.BackgroundColor = _originalBackground;
        }

        public virtual void ClearScreen()
        {
            Console.Clear();
        }
    }
}