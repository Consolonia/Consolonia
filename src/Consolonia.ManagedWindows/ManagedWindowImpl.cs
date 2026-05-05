using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.ManagedWindows
{
    /// <summary>
    ///     Wraps a ManagedWindow as an IWindowImpl, enabling standard Avalonia Window.ShowDialog()
    ///     to work in the console by hosting the window content inside a ManagedWindow on the WindowsPanel.
    /// </summary>
    [SuppressMessage("CA1711", "CA1711")]
    public sealed class ManagedWindowImpl : ManagedWindow, IWindowImpl
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
        private IWindowImpl _parentWindow;
        private IInputRoot _inputRoot;
        private Size _clientSize;
        private bool _disposing;

        public ManagedWindowImpl(IWindowImpl mainWindow)
        {
            base.Content = new Panel();
            _mainWindow = mainWindow;

            // ManagedWindow events → IWindowImpl callbacks
            base.Closed += (_, _) => ((IWindowImpl)this).Closed?.Invoke();
            base.Activated += (_, _) => ((IWindowBaseImpl)this).Activated?.Invoke();
            base.Deactivated += (_, _) => ((IWindowBaseImpl)this).Deactivated?.Invoke();
            base.PositionChanged += (_, e) => ((IWindowBaseImpl)this).PositionChanged?.Invoke(e.Point);
            base.Resized += (_, e) =>
            {
                _clientSize = e.ClientSize;
                ((ITopLevelImpl)this).Resized?.Invoke(e.ClientSize, e.Reason);
            };
            base.Closing += (_, e) =>
            {
                if (_disposing)
                    return;

                var closing = ((IWindowImpl)this).Closing;
                if (closing != null && !closing.Invoke(e.CloseReason))
                    e.Cancel = true;
            };

            // Propagate terminal resize
            _mainWindow.Resized += (size, reason) => ((ITopLevelImpl)this).Resized?.Invoke(size, reason);
        }

        // --- ITopLevelImpl properties ---
        public new Size ClientSize => _clientSize;
        public Size? FrameSize => _clientSize;
        public double RenderScaling => 1;
        public IEnumerable<object> Surfaces => _mainWindow.Surfaces;
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
        public new WindowState WindowState { get; set; }
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
        ///     Moves content from the Avalonia Window into this ManagedWindow.
        /// </summary>
        private void AdoptContentFromSource()
        {
            // In Avalonia 11.x, the inputRoot IS the Window
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

            // Move content from the Window to this ManagedWindow
            var content = win.Content;
            win.Content = null;
            this.DataContext = win.DataContext;
            this.Content = content;

            // Make the original Window invisible so its empty template doesn't render
            // as an artifact. Use Opacity instead of IsVisible so Avalonia still considers
            // it "visible" (required for ShowDialog owner checks).
            win.Opacity = 0;

            // Dispose the original Window's LayoutManager so it can't run
            // stale queued arrange/measure operations for controls we moved out.
            var layoutManagerProp = typeof(TopLevel).GetProperty("LayoutManager",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (layoutManagerProp?.GetValue(win) is IDisposable layoutManager)
                layoutManager.Dispose();
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
            // Move content before Show so it's in our tree before any layout pass.
            AdoptContentFromSource();

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
        }

        public void Hide()
        {
            base.Close();
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

            _clientSize = clientSize;
            try
            {
                base.ClientSize = clientSize;
            }
            catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException)
            {
                // ManagedWindow template may not be applied yet
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
            base.Close();
            GC.SuppressFinalize(this);
        }
    }
}
