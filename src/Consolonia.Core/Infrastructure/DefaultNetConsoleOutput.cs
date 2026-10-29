using System;
using System.Runtime.CompilerServices;
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
    public class DefaultNetConsoleOutput : PauseBase, IConsoleOutput
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

        public int CellPixelWidth => DefaultCellPixelSize.Width;

        public int CellPixelHeight => DefaultCellPixelSize.Height;

        public virtual void SetTitle(string title)
        {
            WaitPauseTaskIfNecessary();
            Console.Title = title;
        }

        public virtual void SetCaretPosition(PixelBufferCoordinate bufferPoint)
        {
            WaitPauseTaskIfNecessary();
            lock (this)
            {
                // The text buffered so far goes out wherever the cursor is when it is flushed, so it goes
                // out before the cursor moves. The render target places the caret before the frame's last
                // Flush (one write per frame for the ANSI output); without this the frame's last run of
                // text was written at the caret.
                Flush();

                try
                {
                    MoveCursor(bufferPoint);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // ignore
                    // this happens while resizing.
                }

                // the next WritePixel at this position needs no move of its own
                _currentPosition = bufferPoint;
            }
        }

        public virtual PixelBufferCoordinate GetCaretPosition()
        {
            WaitPauseTaskIfNecessary();
            lock (this)
            {
                (int left, int top) = Console.GetCursorPosition();
                return new PixelBufferCoordinate((ushort)left, (ushort)top);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public virtual void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
        {
            // Width 0 is the trailing placeholder cell of a previously rendered wide glyph.
            // The legacy Console API path must skip it entirely to avoid writing a placeholder char.
            if (pixel.Width == 0)
                return;

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
            WaitPauseTaskIfNecessary();

            lock (this)
            {
                if (_stringBuilder.Length == 0)
                    return;
                // Debug.WriteLine($"[{_currentBufferPoint.X},{_currentBufferPoint.Y}] {_lastForegroundColor} on {_lastBackgroundColor} '{_stringBuilder}'");
                Console.Write(_stringBuilder.ToString());
                _stringBuilder.Clear();
            }
        }

        public virtual void WriteText(string str)
        {
            WaitPauseTaskIfNecessary();

            Console.Write(str);
        }

        public virtual void SetCaretStyle(CaretStyle caretStyle)
        {
            WaitPauseTaskIfNecessary();

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
            WaitPauseTaskIfNecessary();
            Console.CursorVisible = false;
        }

        public virtual void ShowCaret()
        {
            WaitPauseTaskIfNecessary();
            Console.CursorVisible = true;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
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
            WaitPauseTaskIfNecessary();
            Console.Clear();
        }

        /// <summary>Moves the console's cursor; tests replace it, having no console.</summary>
        internal virtual void MoveCursor(PixelBufferCoordinate bufferPoint)
        {
            Console.SetCursorPosition(bufferPoint.X, bufferPoint.Y);
        }
    }
}