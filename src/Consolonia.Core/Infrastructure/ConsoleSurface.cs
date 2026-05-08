using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     The console rendering surface. Owns console I/O, cache, cursor,
    ///     and console event handlers. One per application.
    ///     Input routing is delegated to an optional IInputRouter.
    ///     When no router is set, all events go to the main window.
    /// </summary>
    public class ConsoleSurface
    {
        private readonly IKeyboardDevice _keyboardDevice;
        private ConsoleWindowImpl _mainWindow;
        private Point _cursorPosition;
        private StandardCursorType _cursorType = StandardCursorType.Arrow;

        public ConsoleSurface()
        {
            _keyboardDevice = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
            MouseDevice = AvaloniaLocator.Current.GetService<IMouseDevice>();
            Console = AvaloniaLocator.Current.GetRequiredService<IConsole>();
            _consoleOutput = AvaloniaLocator.Current.GetService<IConsoleOutput>()!;
            InitializeCache(Console.Size.Width, Console.Size.Height);

            Console.Resized += OnConsoleResized;
            Console.KeyEvent += OnKeyEvent;
            Console.TextInputEvent += OnTextInputEvent;
            Console.MouseEvent += OnMouseEvent;
            Console.FocusEvent += OnFocusEvent;
        }

        public IConsole Console { get; }
        public IMouseDevice MouseDevice { get; }

        /// <summary>
        ///     Optional input router for multi-window support.
        ///     When set, routes input to the correct child window.
        ///     When null, all input goes to the main window.
        /// </summary>
        public IInputRouter InputRouter { get; set; }

        // --- Console output ---
        private readonly IConsoleOutput _consoleOutput;
        private Pixel?[,] _cache = null!;

        public event Action<Size, WindowResizeReason> Resized;
        public event Action<ConsoleCursor> CursorChanged;
        public event Action ClearScreenRequested;
        public Action<Rect> Paint { get; set; }

        // --- Main window ---

        public void SetMainWindow(ConsoleWindowImpl mainWindow)
        {
            _mainWindow = mainWindow;
        }

        // --- Console event handlers ---

        private void OnConsoleResized()
        {
            var size = new Size(Console.Size.Width, Console.Size.Height);
            InitializeCache((ushort)size.Width, (ushort)size.Height);
            Resized?.Invoke(size, WindowResizeReason.Unspecified);
        }

        public event Action<bool> FocusEvent;

        private void OnFocusEvent(bool focused)
        {
            FocusEvent?.Invoke(focused);
        }

        private void OnKeyEvent(Key key, char keyChar, RawInputModifiers rawInputModifiers, bool down,
            ulong timeStamp, bool tryAsTextInput)
        {
            var target = InputRouter?.RouteKeyboardEvent();
            var inputRoot = target?.InputRoot ?? _mainWindow?.InputRoot;
            var inputCallback = target?.InputCallback ?? _mainWindow?.Input;

            if (inputRoot == null || inputCallback == null)
                return;

            if (!down)
            {
#pragma warning disable CS0618
                var rawInputEventArgs = new RawKeyEventArgs(_keyboardDevice, timeStamp, inputRoot,
                    RawKeyEventType.KeyUp, key, rawInputModifiers);
#pragma warning restore CS0618
                inputCallback(rawInputEventArgs);
            }
            else
            {
#pragma warning disable CS0618
                var rawInputEventArgs = new RawKeyEventArgs(_keyboardDevice, timeStamp,
                    inputRoot, RawKeyEventType.KeyDown, key, rawInputModifiers);
#pragma warning restore CS0618
                inputCallback(rawInputEventArgs);

                if (tryAsTextInput &&
                    !rawInputEventArgs.Handled
                    && !char.IsControl(keyChar)
                    && !rawInputModifiers.HasFlag(RawInputModifiers.Alt)
                    && !rawInputModifiers.HasFlag(RawInputModifiers.Control))
                    inputCallback(new RawTextInputEventArgs(_keyboardDevice,
                        timeStamp, inputRoot, keyChar.ToString()));
            }
        }

        private void OnTextInputEvent(string text, ulong timeStamp, CanBeHandledEventArgs canBeHandledEventArgs)
        {
            var target = InputRouter?.RouteKeyboardEvent();
            var inputRoot = target?.InputRoot ?? _mainWindow?.InputRoot;
            var inputCallback = target?.InputCallback ?? _mainWindow?.Input;

            if (inputRoot == null || inputCallback == null)
                return;

#pragma warning disable CS0618
            RawTextInputEventArgs rawInputEventArgs = new(_keyboardDevice, timeStamp, inputRoot, text);
#pragma warning restore CS0618
            inputCallback(rawInputEventArgs);

            if (rawInputEventArgs.Handled)
                canBeHandledEventArgs.Handled = true;
        }

        private void OnMouseEvent(RawPointerEventType type, Point point, Vector? wheelDelta,
            RawInputModifiers modifiers)
        {
            ulong timestamp = (ulong)Environment.TickCount64;

            // Query the router for the target window
            var target = InputRouter?.RouteMouseEvent(type, point);
            var inputCallback = target?.InputCallback ?? _mainWindow?.Input;
            var inputRoot = target?.InputRoot ?? _mainWindow?.InputRoot;
            var eventPoint = target?.LocalPoint ?? point;

            if (inputRoot == null || inputCallback == null)
                return;

            switch (type)
            {
                case RawPointerEventType.Move:
                case RawPointerEventType.LeftButtonDown:
                case RawPointerEventType.LeftButtonUp:
                case RawPointerEventType.RightButtonUp:
                case RawPointerEventType.RightButtonDown:
                case RawPointerEventType.MiddleButtonDown:
                case RawPointerEventType.XButton1Down:
                case RawPointerEventType.XButton2Down:
                case RawPointerEventType.NonClientLeftButtonDown:
                case RawPointerEventType.MiddleButtonUp:
                case RawPointerEventType.XButton1Up:
                case RawPointerEventType.XButton2Up:
                    inputCallback(new RawPointerEventArgs(MouseDevice, timestamp, inputRoot,
                        type, eventPoint, modifiers));
                    break;
                case RawPointerEventType.Wheel:
                    inputCallback(new RawMouseWheelEventArgs(MouseDevice, timestamp, inputRoot, eventPoint,
                        (Vector)wheelDelta!, modifiers));
                    break;
            }

            _cursorPosition = point;
            UpdateCursor();
        }

        // --- Cursor ---

        public ConsoleCursor Cursor { get; private set; }

        public void SetCursorType(StandardCursorType cursorType)
        {
            _cursorType = cursorType;
            UpdateCursor();
        }

        public void UpdateCursorPosition(Point point)
        {
            _cursorPosition = point;
            UpdateCursor();
        }

        private void UpdateCursor()
        {
            var newCursor = new ConsoleCursor(
                new PixelBufferCoordinate((ushort)_cursorPosition.X, (ushort)_cursorPosition.Y),
                GetCursorText());
            if (Cursor.CompareTo(newCursor) == 0)
                return;

            // Invalidate cache at old AND new cursor positions
            var oldCursor = Cursor;
            InvalidateCursorCache(oldCursor);
            InvalidateCursorCache(newCursor);

            Cursor = newCursor;
            CursorChanged?.Invoke(newCursor);
        }

        private void InvalidateCursorCache(ConsoleCursor cursor)
        {
            if (cursor.IsEmpty()) return;
            for (int i = 0; i < cursor.Width; i++)
            {
                int cx = cursor.Coordinate.X + i;
                int cy = cursor.Coordinate.Y;
                if (cx >= 0 && cx < _cache.GetLength(0) && cy >= 0 && cy < _cache.GetLength(1))
                    _cache[cx, cy] = null;
            }
            // Also mark dirty so RenderPixelBuffer processes these positions
            _mainWindow?.DirtyRegions.AddRect(new PixelRect(
                cursor.Coordinate.X, cursor.Coordinate.Y, cursor.Width, 1));
        }

        /// <summary>
        ///     Applies the cursor overlay to a pixel at the given screen position.
        ///     Called by RenderTarget during rendering.
        /// </summary>
        public Pixel ApplyCursorOverlay(Pixel pixel, ushort x, ushort y)
        {
            if (Cursor.IsEmpty()) return pixel;
            if (Cursor.Coordinate.Y != y) return pixel;
            if (x < Cursor.Coordinate.X || x >= Cursor.Coordinate.X + Cursor.Width) return pixel;

            if (Cursor.Type == " " && pixel.Width == 1)
            {
                char cursorChar = pixel.Foreground.Symbol.Character != '\0'
                    ? pixel.Foreground.Symbol.Character
                    : ' ';
                return new Pixel(
                    new PixelForeground(new Symbol(cursorChar, 1), pixel.Background.Color),
                    new PixelBackground(GetContrastColor(pixel.Background.Color)));
            }
            else
            {
                char cursorChar = Cursor.Type[x - Cursor.Coordinate.X];
                Color foreground = pixel.Foreground.Color != Colors.Transparent
                    ? pixel.Foreground.Color
                    : GetContrastColor(pixel.Background.Color);
                return new Pixel(
                    new PixelForeground(new Symbol(cursorChar, 1), foreground,
                        pixel.Foreground.Weight, pixel.Foreground.Style, pixel.Foreground.TextDecoration),
                    pixel.Background, pixel.CaretStyle);
            }
        }

        private static Color GetContrastColor(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double contrastWithWhite = (1.0 + 0.05) / (luminance + 0.05);
            double contrastWithBlack = (luminance + 0.05) / (0.0 + 0.05);
            return contrastWithWhite > contrastWithBlack ? Colors.White : Colors.Black;
        }

        private string GetCursorText()
        {
            return _cursorType switch
            {
                StandardCursorType.Arrow => GetDefaultCursor(),
                StandardCursorType.Cross => "+",
                StandardCursorType.Hand => "@",
                StandardCursorType.Help => "?",
                StandardCursorType.No => "X",
                StandardCursorType.SizeAll => "*",
                StandardCursorType.SizeNorthSouth => "^",
                StandardCursorType.SizeWestEast => ">",
                StandardCursorType.Wait => "o",
                StandardCursorType.Ibeam => "I",
                StandardCursorType.UpArrow => "^",
                StandardCursorType.TopSide => "^",
                StandardCursorType.BottomSide => "v",
                StandardCursorType.LeftSide => "<",
                StandardCursorType.RightSide => ">",
                StandardCursorType.TopLeftCorner => @"\",
                StandardCursorType.TopRightCorner => "/",
                StandardCursorType.BottomLeftCorner => "/",
                StandardCursorType.BottomRightCorner => @"\",
                StandardCursorType.DragCopy => "+",
                StandardCursorType.DragLink => "@",
                StandardCursorType.DragMove => ">",
                StandardCursorType.AppStarting => "o",
                _ => " "
            };
        }

        private string GetDefaultCursor()
        {
            if (Console.Capabilities.HasFlag(ConsoleCapabilities.SupportsMouseMove) &&
                !Console.Capabilities.HasFlag(ConsoleCapabilities.SupportsMouseCursor))
                return " ";
            return string.Empty;
        }

        // --- Console output ---

        private void InitializeCache(ushort width, ushort height)
        {
            _cache = new Pixel?[width, height];
            for (ushort y = 0; y < height; y++)
            for (ushort x = 0; x < width; x++)
                _cache[x, y] = Pixel.Empty;
        }

        public void ClearScreen()
        {
            _consoleOutput.ClearScreen();
            _consoleOutput.Flush();
            InitializeCache(Console.Size.Width, Console.Size.Height);
            ClearScreenRequested?.Invoke();
        }

        /// <summary>
        ///     Renders a PixelBuffer to the console output. Applies cursor overlay,
        ///     compares against cache, and writes only changed pixels.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RenderPixelBuffer(PixelBuffer pixelBuffer, Snapshot dirtyRegions)
        {
            _consoleOutput.HideCaret();

            PixelBufferCoordinate? caretPosition = null;
            CaretStyle? caretStyle = null;

            for (ushort y = 0; y < pixelBuffer.Height; y++)
            {
                bool isWide = false;
                for (ushort x = 0; x < pixelBuffer.Width; x++)
                {
                    Pixel pixel = pixelBuffer[x, y];

                    if (pixel.IsCaret())
                    {
                        if (caretPosition != null)
                            throw new InvalidOperationException("Caret is already shown");
                        caretPosition = new PixelBufferCoordinate(x, y);
                        caretStyle = pixel.CaretStyle;
                    }

                    if (!dirtyRegions.Contains(x, y, false))
                        continue;

                    // Apply cursor overlay
                    pixel = ApplyCursorOverlay(pixel, x, y);

                    if (pixel.Width > 1)
                        for (ushort i = 1; i < pixel.Width && x + i < pixelBuffer.Width; i++)
                            if (pixelBuffer[(ushort)(x + i), y].Width != 0)
                            {
                                pixel = new Pixel(
                                    new PixelForeground(Symbol.Space, pixel.Foreground.Color, pixel.Foreground.Weight,
                                        pixel.Foreground.Style, pixel.Foreground.TextDecoration), pixel.Background,
                                    pixel.CaretStyle);
                                break;
                            }

                    {
                        if (pixel.Width > 1)
                            isWide = true;
                        else if (pixel.Width == 1)
                            isWide = false;
                    }

                    if (pixel.Width == 0 && !isWide)
                        pixel = new Pixel(
                            new PixelForeground(Symbol.Space, pixel.Foreground.Color, pixel.Foreground.Weight,
                                pixel.Foreground.Style, pixel.Foreground.TextDecoration), pixel.Background,
                            pixel.CaretStyle);

                    {
                        bool anyDifferent = false;
                        for (ushort i = 0; i < ushort.Max(pixel.Width, 1); i++)
                            if ((i == 0 ? pixel : pixelBuffer[(ushort)(x + i), y]) != _cache[x + i, y])
                            {
                                anyDifferent = true;
                                break;
                            }

                        if (!anyDifferent)
                            continue;
                    }

                    _consoleOutput.WritePixel(new PixelBufferCoordinate(x, y), in pixel);
                    _cache[x, y] = pixel;
                }
            }

            _consoleOutput.Flush();

            if (caretPosition != null && caretStyle != CaretStyle.None)
            {
                _consoleOutput.SetCaretPosition((PixelBufferCoordinate)caretPosition);
                _consoleOutput.SetCaretStyle((CaretStyle)caretStyle!);
                _consoleOutput.ShowCaret();
            }
            else
            {
                _consoleOutput.HideCaret();
            }
        }

        public void Dispose()
        {
            Console.Resized -= OnConsoleResized;
            Console.KeyEvent -= OnKeyEvent;
            Console.TextInputEvent -= OnTextInputEvent;
            Console.MouseEvent -= OnMouseEvent;
            Console.FocusEvent -= OnFocusEvent;
            if (Console is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
