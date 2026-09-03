using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering;
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

        private readonly IWindowImpl _mainWindow;

        /// <summary>
        ///     Held so it can be taken off <see cref="_mainWindow" /> again in <see cref="Dispose" />.
        /// </summary>
        /// <remarks>
        ///     Every other subscription in the constructor is to this object's own base events, so it
        ///     dies with the object. This one is on the MAIN window, which outlives every dialog: an
        ///     anonymous lambda there captures this instance and keeps it alive for the life of the
        ///     application, so a dialog opened and closed a hundred times leaves a hundred handlers on
        ///     the main window, all of them firing on every terminal resize into windows that are gone.
        /// </remarks>
        private readonly Action<Size, WindowResizeReason> _mainWindowResized;
        private Size _clientSize;
        private bool _contentAdopted;
        private bool _disposing;
        private IInputRoot _inputRoot;
        private IWindowImpl _parentWindow;

        public ManagedWindowImpl(IWindowImpl mainWindow)
        {
            Content = new Panel();
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
                // When Dispose() initiates the close (from Avalonia Window.Close()),
                // Avalonia has already processed the Closing check — don't call it again
                // or it may cancel the close and block shutdown.
                if (_disposing)
                    return;

                Func<WindowCloseReason, bool> closing = ((IWindowImpl)this).Closing;
                if (closing != null && !closing.Invoke(e.CloseReason))
                    e.Cancel = true;
            };

            // Propagate terminal resize
            _mainWindowResized = (size, reason) => ((ITopLevelImpl)this).Resized?.Invoke(size, reason);
            _mainWindow.Resized += _mainWindowResized;
        }

        // --- ITopLevelImpl properties ---
        public new Size ClientSize => _clientSize;
        public Size? FrameSize => _clientSize;
        public double RenderScaling => 1;
        IPlatformRenderSurface[] ITopLevelImpl.Surfaces => _mainWindow.Surfaces;
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

        public bool WindowStateGetterIsUsable => true;
        public Action<WindowState> WindowStateChanged { get; set; }
        public Action GotInputWhenDisabled { get; set; }
        Func<WindowCloseReason, bool> IWindowImpl.Closing { get; set; }
        public bool IsClientAreaExtendedToDecorations => false;
        public Action<bool> ExtendClientAreaToDecorationsChanged { get; set; }
        public bool NeedsManagedDecorations => false;
        public PlatformRequestedDrawnDecoration RequestedDrawnDecorations => PlatformRequestedDrawnDecoration.None;
        public Thickness ExtendedMargins => default;
        public Thickness OffScreenMargin => default;

        // --- ITopLevelImpl methods ---
        public void SetInputRoot(IInputRoot inputRoot)
        {
            _inputRoot = inputRoot;
        }

        public Point PointToClient(PixelPoint point)
        {
            return point.ToPoint(1);
        }

        public PixelPoint PointToScreen(Point point)
        {
            return new PixelPoint((int)point.X, (int)point.Y);
        }

        public void SetCursor(ICursorImpl cursor)
        {
        }

        public IPopupImpl CreatePopup()
        {
            return null;
        }

        public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels)
        {
        }

        public void SetFrameThemeVariant(PlatformThemeVariant themeVariant)
        {
        }

        public object TryGetFeature(Type featureType)
        {
            return _mainWindow.TryGetFeature(featureType);
        }

        // --- IWindowBaseImpl methods ---
        public void Show(bool activate, bool isDialog)
        {
            // Move content before Show so it's in our tree before any layout pass.
            AdoptContentFromSource();

            // Clamp to terminal screen size, but respect SizeToContent
            Size maxSize = _mainWindow.ClientSize;
            SizeToContent sizeToContent = SizeToContent;
            if (sizeToContent != SizeToContent.Width && sizeToContent != SizeToContent.WidthAndHeight)
                if (Width > maxSize.Width || double.IsNaN(Width))
                    Width = maxSize.Width;

            if (sizeToContent != SizeToContent.Height && sizeToContent != SizeToContent.WidthAndHeight)
                if (Height > maxSize.Height || double.IsNaN(Height))
                    Height = maxSize.Height;
            if (_clientSize.Width > maxSize.Width || _clientSize.Height > maxSize.Height)
                _clientSize = new Size(
                    Math.Min(_clientSize.Width, maxSize.Width),
                    Math.Min(_clientSize.Height, maxSize.Height));

            ShowActivated = activate;
            if (isDialog)
            {
                // Pass the parent ManagedWindowImpl so nested dialogs work correctly
                var parent = _parentWindow as ManagedWindowImpl;
                ShowDialog(parent);
            }
            else
            {
                Show();
            }
        }

        public void Hide()
        {
            // close is done through Closing and Dispose()
            IsVisible = false;
        }

        public void Move(PixelPoint point)
        {
            Position = point;
        }

        public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application)
        {
            Size maxSize = _mainWindow.ClientSize;
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
        public void SetTitle(string title)
        {
            Title = title ?? string.Empty;
        }

        public void SetTopmost(bool value)
        {
            Topmost = value;
        }

        public void SetIcon(IWindowIconImpl icon)
        {
        }

        public void SetWindowDecorations(WindowDecorations enabled)
        {
        }

        public void SetParent(IWindowImpl parent)
        {
            _parentWindow = parent;
        }

        public void SetEnabled(bool enable)
        {
            IsEnabled = enable;
        }

        public void SetMinMaxSize(Size minSize, Size maxSize)
        {
            MaxHeight = maxSize.Height;
            MaxWidth = maxSize.Width;
            MinHeight = minSize.Height;
            MinWidth = minSize.Width;
        }

        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint)
        {
        }

        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight)
        {
        }

        public void ShowTaskbarIcon(bool value)
        {
        }

        public void SetCanMinimize(bool value)
        {
            CanResize = value;
        }

        public void SetCanMaximize(bool value)
        {
            CanResize = value;
        }

        void IWindowImpl.CanResize(bool value)
        {
            CanResize = value;
        }

        public void BeginMoveDrag(PointerPressedEventArgs e)
        {
        }

        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
        {
        }

        public void Dispose()
        {
            if (_disposing)
                return;
            _disposing = true;

            // Before the close, so a resize arriving during it is not propagated into a window that
            // is on its way out. Resized is a PROPERTY holding a delegate rather than an event, so
            // this is Delegate.Remove: it takes off this instance's handler and leaves any other
            // subscriber's in place.
            _mainWindow.Resized -= _mainWindowResized;

            Close();
        }

        public static string GetTextIcon(Window window)
        {
            return window.GetValue(TextIconProperty);
        }

        public static void SetTextIcon(Window window, string value)
        {
            window.SetValue(TextIconProperty, value);
        }

        /// <summary>
        ///     Moves content from the Avalonia Window into this ManagedWindow.
        /// </summary>
        private void AdoptContentFromSource()
        {
            if (_contentAdopted)
                return;

            // In Avalonia 12 the input root is a PresentationSource whose RootVisual is the
            // TopLevelHost hosted by the Window (same shape ConsoleWindow.SetShowAccessKeys relies on).
            if (_inputRoot is not IPresentationSource presentationSource ||
                presentationSource.RootVisual?.Parent is not Window win)
                return;

            // Bind properties from the Avalonia Window to this ManagedWindow
            this[!TitleProperty] = win[!Window.TitleProperty];
            this[!WindowStartupLocationProperty] = win[!Window.WindowStartupLocationProperty];
            this[!BackgroundProperty] = win[!BackgroundProperty];
            this[!ForegroundProperty] = win[!ForegroundProperty];
            this[!PaddingProperty] = win[!PaddingProperty];
            this[!FontSizeProperty] = win[!FontSizeProperty];
            this[!FontFamilyProperty] = win[!FontFamilyProperty];
            this[!FontWeightProperty] = win[!FontWeightProperty];
            this[!FontStyleProperty] = win[!FontStyleProperty];
            Opacity = win.Opacity;
            this[!FlowDirectionProperty] = win[!FlowDirectionProperty];
            this[!HorizontalContentAlignmentProperty] = win[!HorizontalContentAlignmentProperty];
            this[!VerticalContentAlignmentProperty] = win[!VerticalContentAlignmentProperty];
            this[!MarginProperty] = win[!MarginProperty];
            this[!IsEnabledProperty] = win[!IsEnabledProperty];
            this[!WindowStateProperty] = win[!Window.WindowStateProperty];

            // Pick up the text icon if set
            string textIcon = GetTextIcon(win);
            if (!string.IsNullOrEmpty(textIcon))
                Icon = textIcon;

            // Copy size/position from the Window to the ManagedWindow
            if (!double.IsNaN(win.Width))
                Width = win.Width;
            if (!double.IsNaN(win.Height))
                Height = win.Height;
            if (win.MinWidth > 0)
                MinWidth = win.MinWidth;
            if (win.MinHeight > 0)
                MinHeight = win.MinHeight;
            if (win.MaxWidth < double.PositiveInfinity)
                MaxWidth = win.MaxWidth;
            if (win.MaxHeight < double.PositiveInfinity)
                MaxHeight = win.MaxHeight;
            if (win.Position != default)
                Position = win.Position;
            CanResize = win.CanResize;
            SizeToContent = win.SizeToContent;

            // Move content from the Window to this ManagedWindow
            object content = win.Content;
            win.Content = null;
            DataContext = win.DataContext;
            Content = content;

            // Make the original Window invisible so its empty template doesn't render
            // as an artifact. Use Opacity instead of IsVisible so Avalonia still considers
            // it "visible" (required for ShowDialog owner checks).
            win.Opacity = 0;

            // Dispose the original Window's LayoutManager so it can't run
            // stale queued arrange/measure operations for controls we moved out.
            PropertyInfo layoutManagerProp = typeof(TopLevel).GetProperty("LayoutManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (layoutManagerProp?.GetValue(win) is IDisposable layoutManager)
                layoutManager.Dispose();

            _contentAdopted = true;
        }
    }
}