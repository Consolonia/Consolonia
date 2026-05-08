using Avalonia;
using Avalonia.Input.Raw;
using Consolonia.Core.Infrastructure;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.ManagedWindows
{
    /// <summary>
    ///     Routes console input events to the correct child window.
    ///     Uses WindowsPanel as the single source of truth for window tracking.
    /// </summary>
    internal class ManagedInputRouter : IInputRouter
    {
        private readonly WindowsPanel _panel;
        private ChildWindowImpl _capturedWindow;
        private bool _captureActive;

        public ManagedInputRouter(WindowsPanel panel)
        {
            _panel = panel;
        }

        public InputTarget? RouteKeyboardEvent()
        {
            if (_panel.ActiveWindow is ChildWindowImpl child)
                return new InputTarget
                {
                    InputCallback = child.Input,
                    InputRoot = child.InputRoot,
                    LocalPoint = default
                };
            return null;
        }

        public InputTarget? RouteMouseEvent(RawPointerEventType type, Point screenPoint)
        {
            bool isDown = type is RawPointerEventType.LeftButtonDown
                or RawPointerEventType.RightButtonDown
                or RawPointerEventType.MiddleButtonDown
                or RawPointerEventType.XButton1Down
                or RawPointerEventType.XButton2Down
                or RawPointerEventType.NonClientLeftButtonDown;
            bool isUp = type is RawPointerEventType.LeftButtonUp
                or RawPointerEventType.RightButtonUp
                or RawPointerEventType.MiddleButtonUp
                or RawPointerEventType.XButton1Up
                or RawPointerEventType.XButton2Up;

            if (isDown)
            {
                var (child, localPoint) = HitTest(screenPoint);
                _capturedWindow = child;
                _captureActive = true;

                // Activate the clicked child window
                if (child != null && !child.IsActive)
                    child.Activate();

                if (child != null)
                    return new InputTarget
                    {
                        InputCallback = child.Input,
                        InputRoot = child.InputRoot,
                        LocalPoint = localPoint
                    };
                return null;
            }

            if (_captureActive)
            {
                var target = _capturedWindow;
                if (isUp)
                {
                    _captureActive = false;
                    _capturedWindow = null;
                }

                if (target != null)
                {
                    var localPoint = target.ComputeLocalPoint(screenPoint);
                    return new InputTarget
                    {
                        InputCallback = target.Input,
                        InputRoot = target.InputRoot,
                        LocalPoint = localPoint
                    };
                }

                return null;
            }

            // Not captured — hit test for move events
            var (hitChild, hitLocal) = HitTest(screenPoint);
            if (hitChild != null)
                return new InputTarget
                {
                    InputCallback = hitChild.Input,
                    InputRoot = hitChild.InputRoot,
                    LocalPoint = hitLocal
                };
            return null;
        }

        private (ChildWindowImpl child, Point localPoint) HitTest(Point screenPoint)
        {
            var children = _panel.Windows;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] is not ChildWindowImpl child)
                    continue;

                var contentPos = child.ContentPosition;
                var contentSize = child.ContentAreaSize;
                double localX = screenPoint.X - contentPos.X;
                double localY = screenPoint.Y - contentPos.Y;

                // Content area hit — route to this child
                if (localX >= 0 && localX < contentSize.Width &&
                    localY >= 0 && localY < contentSize.Height)
                    return (child, new Point(localX, localY));

                // Chrome blocking: if point is within full window bounds (including chrome),
                // block lower windows from receiving the event — route to main window.
                var fullBounds = new Rect(child.Position.X, child.Position.Y,
                    child.Bounds.Width, child.Bounds.Height);
                if (fullBounds.Contains(screenPoint))
                    return (null, default);
            }

            return (null, default);
        }
    }
}
