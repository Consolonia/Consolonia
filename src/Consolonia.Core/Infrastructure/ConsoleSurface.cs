using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     The console rendering surface. Owns the PixelBuffer, DirtyRegions,
    ///     console I/O, and input routing. One per application.
    ///     Windows (ConsoleWindowImpl, ManagedWindowImpl) are viewports into this surface.
    /// </summary>
    public class ConsoleSurface
    {
        private readonly IKeyboardDevice _keyboardDevice;
        private readonly List<IPixelBufferWindow> _windows = new();
        private IPixelBufferWindow _pointerCaptureWindow;
        private bool _pointerCaptureActive;
        private Point _cursorPosition;
        private StandardCursorType _cursorType = StandardCursorType.Arrow;

        public ConsoleSurface()
        {
            _keyboardDevice = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
            MouseDevice = AvaloniaLocator.Current.GetService<IMouseDevice>();
            Console = AvaloniaLocator.Current.GetRequiredService<IConsole>();
            _consoleOutput = AvaloniaLocator.Current.GetService<IConsoleOutput>()!;
            Compositor = new Compositor(null);
            InitializeCache(Console.Size.Width, Console.Size.Height);

            Console.Resized += OnConsoleResized;
            Console.KeyEvent += OnKeyEvent;
            Console.TextInputEvent += OnTextInputEvent;
            Console.MouseEvent += OnMouseEvent;
            Console.FocusEvent += OnFocusEvent;
        }

        public IConsole Console { get; }
        public Compositor Compositor { get; }
        public IMouseDevice MouseDevice { get; }

        // --- Console output ---
        private readonly IConsoleOutput _consoleOutput;
        private Pixel?[,] _cache = null!;

        public event Action<Size, WindowResizeReason> Resized;
        public event Action<ConsoleCursor> CursorChanged;
        public event Action ClearScreenRequested;
        public Action<Rect> Paint { get; set; }



        // --- Window registry (for input routing) ---

        public void RegisterWindow(IPixelBufferWindow window)
        {
            _windows.Add(window);
        }

        public void UnregisterWindow(IPixelBufferWindow window)
        {
            _windows.Remove(window);
        }

        private IPixelBufferWindow GetActiveChildWindow()
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (_windows[i].IsActive)
                    return _windows[i];
            }
            return null;
        }

        private IPixelBufferWindow HitTestChildWindow(Point point, out Point localPoint)
        {
            // Iterate topmost first.
            // 1. If point is in content area → route to that child
            // 2. If point is in full window bounds (chrome) → route to main window (null)
            //    This prevents lower windows from stealing clicks on higher windows' chrome.
            // 3. If point is outside entirely → check next window
            for (int i = _windows.Count - 1; i > 0; i--) // skip index 0 (main window)
            {
                var child = _windows[i];
                var pos = child.Position;
                var contentSize = child.ContentSize;

                double localX = point.X - pos.X;
                double localY = point.Y - pos.Y;

                // Content area hit — route to child
                if (localX >= 0 && localX < contentSize.Width &&
                    localY >= 0 && localY < contentSize.Height)
                {
                    localPoint = new Point(localX, localY);
                    return child;
                }

                // Check full window bounds (including chrome).
                // If point is within the full bounds but not content, it's on chrome.
                // Route to main window to prevent lower windows from stealing the click.
                var fullBounds = child.FullBounds;
                if (fullBounds.Contains(new PixelPoint((int)point.X, (int)point.Y)))
                {
                    localPoint = default;
                    return null;
                }
            }
            localPoint = default;
            return null;
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
            var activeChild = GetActiveChildWindow();
            var inputRoot = activeChild?.InputRoot ?? (_windows.Count > 0 ? _windows[0] : null)?.InputRoot;
            var inputCallback = activeChild?.InputCallback ?? (_windows.Count > 0 ? _windows[0] : null)?.InputCallback;

            System.Diagnostics.Debug.WriteLine($"[ConsoleSurface] OnKeyEvent: key={key} down={down} activeChild={activeChild?.GetType().Name} inputRoot={inputRoot != null} inputCallback={inputCallback != null} windows={_windows.Count}");

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
            var activeChild = GetActiveChildWindow();
            var inputRoot = activeChild?.InputRoot ?? (_windows.Count > 0 ? _windows[0] : null)?.InputRoot;
            var inputCallback = activeChild?.InputCallback ?? (_windows.Count > 0 ? _windows[0] : null)?.InputCallback;

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

            bool isButtonDown = type is RawPointerEventType.LeftButtonDown
                or RawPointerEventType.RightButtonDown
                or RawPointerEventType.MiddleButtonDown
                or RawPointerEventType.XButton1Down
                or RawPointerEventType.XButton2Down
                or RawPointerEventType.NonClientLeftButtonDown;
            bool isButtonUp = type is RawPointerEventType.LeftButtonUp
                or RawPointerEventType.RightButtonUp
                or RawPointerEventType.MiddleButtonUp
                or RawPointerEventType.XButton1Up
                or RawPointerEventType.XButton2Up;

            if (isButtonDown)
            {
                _pointerCaptureWindow = HitTestChildWindow(point, out _);
                _pointerCaptureActive = true;
            }

            IPixelBufferWindow targetWindow;
            Point eventPoint;
            if (_pointerCaptureActive)
            {
                targetWindow = _pointerCaptureWindow;
                if (targetWindow != null)
                    eventPoint = new Point(point.X - targetWindow.Position.X, point.Y - targetWindow.Position.Y);
                else
                    eventPoint = point;
            }
            else
            {
                targetWindow = HitTestChildWindow(point, out Point localPoint);
                eventPoint = targetWindow != null ? localPoint : point;
            }

            var mainWindow = _windows.Count > 0 ? _windows[0] : null;
            var inputRoot = targetWindow?.InputRoot ?? mainWindow?.InputRoot;
            var inputCallback = targetWindow?.InputCallback ?? mainWindow?.InputCallback;

            if (isButtonUp)
            {
                _pointerCaptureWindow = null;
                _pointerCaptureActive = false;
            }

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
            if (_windows.Count > 0)
                _windows[0].DirtyRegions.AddRect(new PixelRect(
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
            // DirtyRegions are per-window, no surface-level cleanup needed
            if (Console is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
