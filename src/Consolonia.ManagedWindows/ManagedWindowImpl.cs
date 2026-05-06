using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.ManagedWindows
{
    /// <summary>
    ///     Wraps a ManagedWindow as an IWindowImpl and IPixelBufferSurface,
    ///     enabling standard Avalonia Window.ShowDialog() to work in the console.
    ///     Each child window owns its own PixelBuffer and renders independently.
    ///     The main window composites all child PixelBuffers during RenderToDevice().
    /// </summary>
    [SuppressMessage("CA1711", "CA1711")]
    public sealed class ManagedWindowImpl : ManagedWindow, IWindowImpl, IPixelBufferSurface
    {
        /// <summary>
        ///     Attached property for setting a text-based icon on a Window.
        ///     ManagedWindow.Icon is object?, so this text will be used directly.
        /// </summary>
        public static readonly AttachedProperty<string> TextIconProperty =
            AvaloniaProperty.RegisterAttached<ManagedWindowImpl, Window, string>("TextIcon");

        public static string GetTextIcon(Window window) => window.GetValue(TextIconProperty);
        public static void SetTextIcon(Window window, string value) => window.SetValue(TextIconProperty, value);

        private readonly IWindowImpl _mainWindow;
        private readonly ConsoleWindowImpl _mainConsoleWindow;
        private IWindowImpl _parentWindow;
        private IInputRoot _inputRoot;
        private Size _clientSize;
        private bool _disposing;
        private bool _propertiesBound;

        public ManagedWindowImpl(IWindowImpl mainWindow)
        {
            base.Content = new Panel();
            _mainWindow = mainWindow;
            _mainConsoleWindow = (ConsoleWindowImpl)mainWindow;

            // Initialize PixelBuffer with a default size (will be resized by Avalonia)
            PixelBuffer = new PixelBuffer(1, 1);

            // ManagedWindow events → IWindowImpl callbacks
            base.Closed += (_, _) => ((IWindowImpl)this).Closed?.Invoke();
            base.Activated += (_, _) =>
            {
                _isActive = true;
                ((IWindowBaseImpl)this).Activated?.Invoke();
            };
            base.Deactivated += (_, _) =>
            {
                _isActive = false;
                ((IWindowBaseImpl)this).Deactivated?.Invoke();
            };
            base.PositionChanged += (_, e) =>
            {
                ((IWindowBaseImpl)this).PositionChanged?.Invoke(e.Point);
                UpdateSurfacePosition();
            };
            base.Resized += (_, e) =>
            {
                // e.ClientSize is the actual content presenter bounds (excludes chrome).
                // This is what Avalonia should render to — our "client area".
                _clientSize = e.ClientSize;
                var width = (ushort)Math.Max(1, e.ClientSize.Width);
                var height = (ushort)Math.Max(1, e.ClientSize.Height);
                if (PixelBuffer.Width != width || PixelBuffer.Height != height)
                {
                    PixelBuffer = new PixelBuffer(width, height);
                    DirtyRegions.AddRect(PixelBuffer.Size);
                    _surfaceResized?.Invoke(e.ClientSize, e.Reason);
                }
                // Notify Avalonia of the content area size so it layouts correctly
                ((ITopLevelImpl)this).Resized?.Invoke(e.ClientSize, e.Reason);
                UpdateSurfacePosition();
            };
            base.Closing += (_, e) =>
            {
                if (_disposing)
                    return;

                var closing = ((IWindowImpl)this).Closing;
                if (closing != null && !closing.Invoke(e.CloseReason))
                    e.Cancel = true;
            };
        }

        // --- IPixelBufferSurface implementation ---
        public PixelBuffer PixelBuffer { get; private set; }

        public Snapshot.Regions DirtyRegions { get; } = new();

        IConsole IPixelBufferSurface.Console => _mainConsoleWindow.Console;

        PixelPoint IPixelBufferSurface.Position => _surfacePosition;
        private PixelPoint _surfacePosition;

        int IPixelBufferSurface.ZIndex => base.ZIndex;

        private bool _isActive;
        bool IPixelBufferSurface.IsActive => _isActive;

        Action<RawInputEventArgs> IPixelBufferSurface.InputCallback => Input;

        IInputRoot IPixelBufferSurface.InputRoot => _inputRoot;

        private event Action<Size, WindowResizeReason> _surfaceResized;
        event Action<Size, WindowResizeReason> IPixelBufferSurface.Resized
        {
            add => _surfaceResized += value;
            remove => _surfaceResized -= value;
        }

        public event Action<ConsoleCursor> CursorChanged;
        public event Action ClearScreenRequested;

        // --- ITopLevelImpl properties ---
        public new Size ClientSize => _clientSize;
        public Size? FrameSize => _clientSize;
        public double RenderScaling => 1;
        public IEnumerable<object> Surfaces => [this];
        public Action<RawInputEventArgs> Input { get; set; }
        public Action<Rect> Paint { get; set; }
        Action<Size, WindowResizeReason> ITopLevelImpl.Resized { get; set; }
        public Action<double> ScalingChanged { get; set; }
        public Action<WindowTransparencyLevel> TransparencyLevelChanged { get; set; }
        public Compositor Compositor => _mainWindow.Compositor;
        Action ITopLevelImpl.Closed { get; set; }
        Action ITopLevelImpl.LostFocus { get; set; }
        public WindowTransparencyLevel TransparencyLevel => WindowTransparencyLevel.None;
        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => new(1, 1, 1);

        // --- IWindowBaseImpl properties ---
        public double DesktopScaling => 1d;
        public IPlatformHandle Handle => _mainWindow.Handle;
        public Size MaxAutoSizeHint => _mainWindow.MaxAutoSizeHint;
        Action<PixelPoint> IWindowBaseImpl.PositionChanged { get; set; }
        Action IWindowBaseImpl.Deactivated { get; set; }
        Action IWindowBaseImpl.Activated { get; set; }

        // --- IWindowImpl properties ---
        public new WindowState WindowState
        {
            get => base.WindowState;
            set => base.WindowState = value;
        }
        public Action<WindowState> WindowStateChanged { get; set; }
        public Action GotInputWhenDisabled { get; set; }
        Func<WindowCloseReason, bool> IWindowImpl.Closing { get; set; }
        public bool IsClientAreaExtendedToDecorations => false;
        public Action<bool> ExtendClientAreaToDecorationsChanged { get; set; }
        public bool NeedsManagedDecorations => false;
        public Thickness ExtendedMargins => default;
        public Thickness OffScreenMargin => default;

        // --- ITopLevelImpl methods ---
        public void SetInputRoot(IInputRoot inputRoot)
        {
            _inputRoot = inputRoot;
        }

        /// <summary>
        ///     Binds properties from the Avalonia Window to this ManagedWindow for chrome display.
        /// </summary>
        private void BindWindowProperties()
        {
            if (_propertiesBound)
                return;

            if (_inputRoot is not Window win)
                return;

            // Bind properties from the Avalonia Window to this ManagedWindow
            this[!TitleProperty] = win[!Window.TitleProperty];
            this[!WindowStartupLocationProperty] = win[!Window.WindowStartupLocationProperty];
            this[!BackgroundProperty] = win[!Window.BackgroundProperty];
            this[!ForegroundProperty] = win[!Window.ForegroundProperty];
            this[!PaddingProperty] = win[!Window.PaddingProperty];
            this[!FontSizeProperty] = win[!Window.FontSizeProperty];
            this[!FontFamilyProperty] = win[!Window.FontFamilyProperty];
            this[!FontWeightProperty] = win[!Window.FontWeightProperty];
            this[!FontStyleProperty] = win[!Window.FontStyleProperty];
            this.Opacity = win.Opacity;
            this[!FlowDirectionProperty] = win[!Visual.FlowDirectionProperty];
            this[!HorizontalContentAlignmentProperty] = win[!Window.HorizontalContentAlignmentProperty];
            this[!VerticalContentAlignmentProperty] = win[!Window.VerticalContentAlignmentProperty];
            this[!MarginProperty] = win[!Window.MarginProperty];
            this[!IsEnabledProperty] = win[!Window.IsEnabledProperty];
            this[!WindowStateProperty] = win[!Window.WindowStateProperty];

            // Pick up the text icon if set
            var textIcon = GetTextIcon(win);
            if (!string.IsNullOrEmpty(textIcon))
                this.Icon = textIcon;

            // Copy size/position from the Window to the ManagedWindow
            if (!double.IsNaN(win.Width))
                this.Width = win.Width;
            if (!double.IsNaN(win.Height))
                this.Height = win.Height;
            if (win.MinWidth > 0)
                this.MinWidth = win.MinWidth;
            if (win.MinHeight > 0)
                this.MinHeight = win.MinHeight;
            if (win.MaxWidth < double.PositiveInfinity)
                this.MaxWidth = win.MaxWidth;
            if (win.MaxHeight < double.PositiveInfinity)
                this.MaxHeight = win.MaxHeight;
            if (win.Position != default)
                this.Position = win.Position;
            this.CanResize = win.CanResize;
            this.SizeToContent = win.SizeToContent;

            _propertiesBound = true;
        }

        /// <summary>
        ///     Updates the surface position based on the ManagedWindow's position.
        ///     The content area is offset from the window position by chrome (title bar, border).
        /// </summary>
        private void UpdateSurfacePosition()
        {
            // Mark the old position as dirty so stale content gets cleared
            var oldPos = _surfacePosition;
            var buf = PixelBuffer;
            if (buf.Width > 0 && buf.Height > 0)
            {
                _mainConsoleWindow.DirtyRegions.AddRect(
                    new PixelRect(oldPos.X, oldPos.Y, buf.Width, buf.Height));
            }

            // Content area is offset from ManagedWindow.Position by border + title bar.
            // BorderThickness comes from the theme (typically 1 on all sides).
            // Title bar is 1 row.
            var border = this.BorderThickness;
            int chromeLeft = (int)border.Left;
            int chromeTop = (int)border.Top + 1; // +1 for title bar row
            _surfacePosition = new PixelPoint(Position.X + chromeLeft, Position.Y + chromeTop);

            // Mark the new position as dirty so content gets drawn there
            if (buf.Width > 0 && buf.Height > 0)
            {
                _mainConsoleWindow.DirtyRegions.AddRect(
                    new PixelRect(_surfacePosition.X, _surfacePosition.Y, buf.Width, buf.Height));
            }
        }

        public Point PointToClient(PixelPoint point) => point.ToPoint(1);
        public PixelPoint PointToScreen(Point point) => new((int)point.X, (int)point.Y);
        public void SetCursor(ICursorImpl cursor) { }
        public IPopupImpl CreatePopup() => null;
        public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels) { }

        public void SetFrameThemeVariant(PlatformThemeVariant themeVariant) { }

        public object TryGetFeature(Type featureType)
        {
            return _mainWindow.TryGetFeature(featureType);
        }

        public void GetWindowsZOrder(Span<Window> windows, Span<long> zOrder)
        {
            for (int i = 0; i < zOrder.Length; i++) zOrder[i] = 0;
        }

        // --- IWindowBaseImpl methods ---
        public void Show(bool activate, bool isDialog)
        {
            // Bind properties before Show so chrome displays correctly.
            BindWindowProperties();

            // Clamp to terminal screen size, but respect SizeToContent
            var maxSize = _mainWindow.ClientSize;
            var sizeToContent = this.SizeToContent;
            if (sizeToContent != SizeToContent.Width && sizeToContent != SizeToContent.WidthAndHeight)
            {
                if (this.Width > maxSize.Width || double.IsNaN(this.Width))
                    this.Width = maxSize.Width;
            }

            if (sizeToContent != SizeToContent.Height && sizeToContent != SizeToContent.WidthAndHeight)
            {
                if (this.Height > maxSize.Height || double.IsNaN(this.Height))
                    this.Height = maxSize.Height;
            }
            if (_clientSize.Width > maxSize.Width || _clientSize.Height > maxSize.Height)
                _clientSize = new Size(
                    Math.Min(_clientSize.Width, maxSize.Width),
                    Math.Min(_clientSize.Height, maxSize.Height));

            // Register with main window for compositing
            _mainConsoleWindow.RegisterChildSurface(this);

            this.ShowActivated = activate;
            if (isDialog)
            {
                // Pass the parent ManagedWindowImpl so nested dialogs work correctly
                var parent = _parentWindow as ManagedWindowImpl;
                base.ShowDialog(parent);
            }
            else
            {
                base.Show();
            }

            UpdateSurfacePosition();
        }

        public void Hide()
        {
            this.IsVisible = false;
        }

        public new void Activate()
        {
            base.Activate();
        }

        public void Move(PixelPoint point) => Position = point;

        public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application)
        {
            var maxSize = _mainWindow.ClientSize;
            clientSize = new Size(
                Math.Min(clientSize.Width, maxSize.Width),
                Math.Min(clientSize.Height, maxSize.Height));

            try
            {
                // Setting base.ClientSize triggers ManagedWindow's Resized event,
                // which updates _clientSize to the content area size and resizes PixelBuffer.
                // But set _clientSize here too as fallback in case Resized doesn't fire yet.
                _clientSize = clientSize;
                base.ClientSize = clientSize;
            }
            catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException)
            {
                // ManagedWindow template may not be applied yet
                var width = (ushort)Math.Max(1, clientSize.Width);
                var height = (ushort)Math.Max(1, clientSize.Height);
                if (PixelBuffer.Width != width || PixelBuffer.Height != height)
                {
                    PixelBuffer = new PixelBuffer(width, height);
                    DirtyRegions.AddRect(PixelBuffer.Size);
                    _surfaceResized?.Invoke(clientSize, reason);
                }
            }
        }

        // --- IWindowImpl methods ---
        public void SetTitle(string title) => Title = title ?? string.Empty;
        public void SetTopmost(bool value) => Topmost = value;
        public void SetIcon(IWindowIconImpl icon) { }
        public void SetSystemDecorations(SystemDecorations enabled) { }
        public void SetParent(IWindowImpl parent) => _parentWindow = parent;
        public void SetEnabled(bool enable) => base.IsEnabled = enable;

        public void SetMinMaxSize(Size minSize, Size maxSize)
        {
            base.MaxHeight = maxSize.Height;
            base.MaxWidth = maxSize.Width;
            base.MinHeight = minSize.Height;
            base.MinWidth = minSize.Width;
        }

        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint) { }
        public void SetExtendClientAreaChromeHints(ExtendClientAreaChromeHints hints) { }
        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight) { }
        public void ShowTaskbarIcon(bool value) { }
        public void SetCanMinimize(bool value) => CanResize = value;
        public void SetCanMaximize(bool value) => CanResize = value;
        void IWindowImpl.CanResize(bool value) => CanResize = value;

        public void BeginMoveDrag(PointerPressedEventArgs e) { }
        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) { }

        public void Dispose()
        {
            if (_disposing)
                return;
            _disposing = true;
            _mainConsoleWindow.UnregisterChildSurface(this);
            base.Close();
            GC.SuppressFinalize(this);
        }
    }
}
