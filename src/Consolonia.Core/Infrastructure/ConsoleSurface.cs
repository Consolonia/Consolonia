using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
            FrameBuffer = new PixelBuffer(Console.Size);
            Compositor = new Compositor(null);

            Console.Resized += OnConsoleResized;
            // Input routing is temporarily handled directly by ConsoleWindowImpl
            // TODO: Move input routing back to ConsoleSurface once focus issue is resolved
            // Console.KeyEvent += OnKeyEvent;
            // Console.TextInputEvent += OnTextInputEvent;
            // Console.MouseEvent += OnMouseEvent;
            Console.FocusEvent += OnFocusEvent;
        }

        /// <summary>Frame buffer — the final composited output written to console.</summary>
        public PixelBuffer FrameBuffer { get; private set; }
        public IConsole Console { get; }
        public Compositor Compositor { get; }
        public IMouseDevice MouseDevice { get; }

        public event Action<Size, WindowResizeReason> Resized;
        public event Action<ConsoleCursor> CursorChanged;
        public event Action ClearScreenRequested;
        public Action<Rect> Paint { get; set; }

        /// <summary>
        ///     Composites all window PixelBuffers bottom-up into the frame buffer,
        ///     then calls RenderToDevice on the provided RenderTarget to flush to console.
        /// </summary>
        internal void CompositeAndRenderToDevice(Drawing.RenderTarget renderTarget)
        {
            var frame = FrameBuffer;

            // Composite all windows bottom-up
            for (int wi = 0; wi < _windows.Count; wi++)
            {
                var window = _windows[wi];
                var buf = window.PixelBuffer;
                var pos = window.Position;

                for (ushort y = 0; y < buf.Height; y++)
                {
                    int screenY = pos.Y + y;
                    if (screenY < 0 || screenY >= frame.Height) continue;

                    for (ushort x = 0; x < buf.Width; x++)
                    {
                        int screenX = pos.X + x;
                        if (screenX < 0 || screenX >= frame.Width) continue;

                        Pixel pixel = buf[x, y];
                        // Skip transparent/unrendered pixels from child windows
                        if (wi > 0 && pixel.Background.Color == Avalonia.Media.Colors.Transparent
                                   && pixel.Foreground.Color == Avalonia.Media.Colors.Transparent)
                            continue;

                        frame[(ushort)screenX, (ushort)screenY] = pixel;
                    }
                }
            }

            renderTarget.RenderFrameToDevice(frame);
        }

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
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var child = _windows[i];
                var pos = child.Position;
                var contentSize = child.ContentSize;
                double localX = point.X - pos.X;
                double localY = point.Y - pos.Y;
                if (localX >= 0 && localX < contentSize.Width &&
                    localY >= 0 && localY < contentSize.Height)
                {
                    localPoint = new Point(localX, localY);
                    return child;
                }
            }
            localPoint = default;
            return null;
        }

        // --- Console event handlers ---

        private void OnConsoleResized()
        {
            var size = new Size(Console.Size.Width, Console.Size.Height);
            FrameBuffer = new PixelBuffer((ushort)size.Width, (ushort)size.Height);
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

        public void SetCursorType(StandardCursorType cursorType)
        {
            _cursorType = cursorType;
            UpdateCursor();
        }

        private void UpdateCursor()
        {
            CursorChanged?.Invoke(
                new ConsoleCursor(
                    new PixelBufferCoordinate((ushort)_cursorPosition.X, (ushort)_cursorPosition.Y),
                    GetCursorText()));
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
                StandardCursorType.SizeNorthSouth => "^v",
                StandardCursorType.SizeWestEast => "<>",
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

        // --- Surface operations ---

        public void ClearScreen()
        {
            ClearScreenRequested?.Invoke();
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
