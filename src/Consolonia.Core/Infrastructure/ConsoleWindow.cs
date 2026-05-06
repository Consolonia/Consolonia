using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Remoting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Window = Avalonia.Controls.Window;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     ConsoleWindowImpl - The main window IWindowImpl. A viewport into the ConsoleSurface at (0,0).
    /// </summary>
#pragma warning disable CA1711
    public class ConsoleWindowImpl : IWindowImpl, IPixelBufferWindow
#pragma warning restore CA1711
    {
        private static bool _singletonGuard;
        private readonly bool _accessKeysAlwaysOn;
        private readonly IDisposable _accessKeysAlwaysOnDisposable;
        private bool _disposedValue;
        private IInputRoot _inputRoot;

        public ConsoleWindowImpl()
        {
            if (_singletonGuard)
                throw new ConsoloniaException(
                    $"Creating multiple {nameof(Window)} objects simultaneously is not allowed. Please use ManagedWindow instead.");

            _singletonGuard = true;

            Surface = new ConsoleSurface();
            Surface.RegisterWindow(this);
            PixelBuffer = new PixelBuffer(Surface.Console.Size);
            DirtyRegions.AddRect(PixelBuffer.Size);
            Surface.Resized += (size, reason) =>
            {
                PixelBuffer = new PixelBuffer((ushort)size.Width, (ushort)size.Height);
                DirtyRegions.AddRect(PixelBuffer.Size);
                Resized?.Invoke(size, reason);
            };
            Surface.FocusEvent += focused =>
            {
                if (focused) Activated?.Invoke();
                else Deactivated?.Invoke();
            };

            Handle = null!;
            _accessKeysAlwaysOn = !Surface.Console.Capabilities.HasFlag(ConsoleCapabilities.SupportsAltSolo);
            if (_accessKeysAlwaysOn)
                _accessKeysAlwaysOnDisposable =
                    AccessText.ShowAccessKeyProperty.Changed.SubscribeAction(OnShowAccessKeyPropertyChanged);
        }

        // --- IPixelBufferWindow ---
        public ConsoleSurface Surface { get; }
        public PixelBuffer PixelBuffer { get; private set; }
        public Snapshot.Regions DirtyRegions { get; } = new();
        PixelPoint IPixelBufferWindow.Position => default;
        Size IPixelBufferWindow.ContentSize => new(PixelBuffer.Width, PixelBuffer.Height);
        bool IPixelBufferWindow.IsActive => true;
        Action<RawInputEventArgs> IPixelBufferWindow.InputCallback => Input;
        IInputRoot IPixelBufferWindow.InputRoot => _inputRoot;

        // --- ITopLevelImpl ---
        public void SetInputRoot(IInputRoot inputRoot)
        {
            _inputRoot = inputRoot;
            if (_accessKeysAlwaysOn)
                _inputRoot.ShowAccessKeys = true;
        }

        public Point PointToClient(PixelPoint point) => point.ToPoint(1);
        public PixelPoint PointToScreen(Point point) => new((int)point.X, (int)point.Y);

        public void SetCursor(ICursorImpl cursor)
        {
            Surface.SetCursorType(cursor is null
                ? StandardCursorType.Arrow
                : ((CursorImpl)cursor).CursorType);
        }

        public IPopupImpl CreatePopup() => null;

        public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels)
        {
            Debug.WriteLine($"ConsoleWindow.SetTransparencyLevelHint({transparencyLevels}) called, not implemented");
        }

        public void SetFrameThemeVariant(PlatformThemeVariant themeVariant)
        {
            switch (themeVariant)
            {
                case PlatformThemeVariant.Dark:
                case PlatformThemeVariant.Light:
                    Debug.WriteLine($"ConsoleWindow.SetFrameThemeVariant({themeVariant}) called, not implemented");
                    break;
            }
        }

        public Size ClientSize
        {
            get
            {
                PixelBufferSize size = Surface.Console.Size;
                return new Size(size.Width, size.Height);
            }
        }

        public Size? FrameSize => ClientSize;
        public double RenderScaling => 1;
        public IEnumerable<object> Surfaces => [this];
        public Action<RawInputEventArgs> Input { get; set; }
        public Action<Rect> Paint { get; set; }
        public Action<Size, WindowResizeReason> Resized { get; set; }
        public Action<double> ScalingChanged { get; set; }
        public Action<WindowTransparencyLevel> TransparencyLevelChanged { get; set; }
        public Compositor Compositor { get; } = new(null);
        public Action Closed { get; set; }
        public Action LostFocus { get; set; }
        public WindowTransparencyLevel TransparencyLevel => WindowTransparencyLevel.None;
        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => new(1, 1, 1);

        // --- IWindowBaseImpl ---
        public void Show(bool activate, bool isDialog)
        {
            if (activate)
                Activated?.Invoke();
        }

        public void Hide() => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowHideNotSupported);
        public void Activate() => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowActivateNotSupported);
        public void SetTopmost(bool value) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetTopmostNotSupported);

        public double DesktopScaling => 1d;
        public PixelPoint Position { get; }
        public Action<PixelPoint> PositionChanged { get; set; }
        public Action Deactivated { get; set; }
        public Action Activated { get; set; }
        public IPlatformHandle Handle { get; }
        public Size MaxAutoSizeHint { get; }

        // --- IWindowImpl ---
        public void SetTitle(string title) => Surface.Console.SetTitle(title);
        public void SetParent(IWindowImpl parent) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetParentNotSupported);
        public void SetEnabled(bool enable) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetEnabledNotSupported);
        public void SetSystemDecorations(SystemDecorations enabled) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetSystemDecorationsNotSupported);
        public void SetIcon(IWindowIconImpl icon) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetIconNotSupported);
        public void ShowTaskbarIcon(bool value) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowShowTaskbarIconNotSupported);
        public void CanResize(bool value) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowCanResizeNotSupported);
        public void BeginMoveDrag(PointerPressedEventArgs e) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowBeginMoveDragNotSupported);
        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowBeginResizeDragNotSupported);
        public void Move(PixelPoint point) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowMoveNotSupported);
        public void SetMinMaxSize(Size minSize, Size maxSize) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetMinMaxSizeNotSupported);
        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetExtendClientAreaToDecorationsHintNotSupported);
        public void SetExtendClientAreaChromeHints(ExtendClientAreaChromeHints hints) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetExtendClientAreaChromeHintsNotSupported);
        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetExtendClientAreaTitleBarHeightHintNotSupported);
        public void SetCanMinimize(bool value) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetCanMinimizeNotSupported);
        public void SetCanMaximize(bool value) => ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowSetCanMaximizeNotSupported);

        public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application)
        {
            if ((int)ClientSize.Width == (int)clientSize.Width && (int)ClientSize.Height == (int)clientSize.Height)
                return;
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.ConsoleWindowBeginResizeNotSupported);
        }

        public WindowState WindowState { get; set; }
        public Action<WindowState> WindowStateChanged { get; set; }
        public Action GotInputWhenDisabled { get; set; }
        public Func<WindowCloseReason, bool> Closing { get; set; }
        public bool IsClientAreaExtendedToDecorations { get; }
        public Action<bool> ExtendClientAreaToDecorationsChanged { get; set; }
        public bool NeedsManagedDecorations { get; }
        public Thickness ExtendedMargins { get; }
        public Thickness OffScreenMargin { get; }

        public object TryGetFeature(Type featureType)
        {
            if (featureType == typeof(IStorageProvider))
            {
                var storageProviderFactory = AvaloniaLocator.Current.GetService<IStorageProviderFactory>();
                return storageProviderFactory?.CreateProvider(null!);
            }

            if (featureType == typeof(IInsetsManager))
                return null;

            if (featureType == typeof(IClipboard))
            {
                var clipboard = AvaloniaLocator.CurrentMutable.GetService<IClipboard>();
                if (clipboard != null)
                    return clipboard;
            }

            if (featureType == typeof(IScreenImpl))
                return new ConsoloniaScreen(new PixelRect(0, 0, Surface.Console.Size.Width, Surface.Console.Size.Height));

            if (featureType == typeof(ILauncher))
            {
                ObjectHandle objHandle =
                    Activator.CreateInstance("Avalonia.Base", "Avalonia.Platform.Storage.FileIO.BclLauncher");
                return (ILauncher)objHandle.Unwrap();
            }

            Debug.WriteLine($"Missing Feature: {featureType.Name} is not implemented but someone is asking for it!");
            return null;
        }

        public void GetWindowsZOrder(Span<Window> windows, Span<long> zOrder)
        {
            for (int i = 0; i < zOrder.Length; i++)
                zOrder[i] = 0;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                _disposedValue = true;
                if (disposing)
                {
                    Closed?.Invoke();
                    _accessKeysAlwaysOnDisposable?.Dispose();
                    Surface.Dispose();
                    _singletonGuard = false;
                }
            }
        }

        private void OnShowAccessKeyPropertyChanged(AvaloniaPropertyChangedEventArgs<bool> args)
        {
            if (args.Sender != _inputRoot) return;
            if (args.GetNewValue<bool>()) return;
            _inputRoot.ShowAccessKeys = true;
        }

        // --- Surface events forwarded as IWindowImpl events ---
        public event Action<ConsoleCursor> CursorChanged
        {
            add => Surface.CursorChanged += value;
            remove => Surface.CursorChanged -= value;
        }

        public event Action ClearScreenRequested
        {
            add => Surface.ClearScreenRequested += value;
            remove => Surface.ClearScreenRequested -= value;
        }

        public void ClearScreen() => Surface.ClearScreen();

        internal IConsole Console => Surface.Console;
    }
}
