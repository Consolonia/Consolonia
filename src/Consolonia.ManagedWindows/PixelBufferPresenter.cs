using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Threading;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.ManagedWindows
{
    /// <summary>
    ///     A control that renders the child window's PixelBuffer content using
    ///     a custom draw operation through Avalonia's rendering pipeline.
    /// </summary>
    internal class PixelBufferPresenter : Control
    {
        private readonly ConsoleWindowImpl _mainWindow;
        internal readonly ChildWindowImpl _childWindow;

        public PixelBufferPresenter(ConsoleWindowImpl mainWindow, ChildWindowImpl childWindow)
        {
            _mainWindow = mainWindow;
            _childWindow = childWindow;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // Measure the child Window's content at full screen size to get its
            // natural desired size. The layout system constrains as needed.
            var childWindow = _childWindow.ChildWindow;
            if (childWindow?.Content is Layoutable content)
            {
                content.Measure(_mainWindow.ClientSize);
                return content.DesiredSize;
            }

            return availableSize;
        }

        public override void Render(DrawingContext context)
        {
            var childBuf = _childWindow.PixelBuffer;
            if (childBuf.Width <= 1 || childBuf.Height <= 1)
                return;

            context.Custom(new PixelBufferDrawOperation(childBuf,
                new Rect(0, 0, childBuf.Width, childBuf.Height), _childWindow));

            // Stay dirty so we re-copy child pixels every frame
            Dispatcher.UIThread.Post(InvalidateVisual);
        }

        private class PixelBufferDrawOperation : ICustomDrawOperation
        {
            private readonly PixelBuffer _buffer;
            private readonly ChildWindowImpl _childWindow;

            public PixelBufferDrawOperation(PixelBuffer buffer, Rect bounds, ChildWindowImpl childWindow)
            {
                _buffer = buffer;
                _childWindow = childWindow;
                Bounds = bounds;
            }

            public Rect Bounds { get; }

            public void Dispose() { }

            public bool HitTest(Point p) => Bounds.Contains(p);

            public bool Equals(ICustomDrawOperation? other) => false;

            public void Render(ImmediateDrawingContext context)
            {
                // ImmediateDrawingContext wraps our DrawingContextImpl
                if (context.TryGetFeature<IDrawingContextImpl>() is DrawingContextImpl impl)
                {
                    // Get the transform offset so DrawPixel writes at the correct screen position.
                    // Also update the child window's surface position for input hit-testing.
                    var offset = new Point(0, 0).Transform(impl.Transform).ToPixelPoint();
                    _childWindow.SetSurfacePosition(offset);

                    for (ushort y = 0; y < _buffer.Height; y++)
                    {
                        for (ushort x = 0; x < _buffer.Width; x++)
                        {
                            impl.DrawPixel(_buffer[x, y],
                                new PixelPoint(x + offset.X, y + offset.Y));
                        }
                    }
                }
            }
        }
    }
}
