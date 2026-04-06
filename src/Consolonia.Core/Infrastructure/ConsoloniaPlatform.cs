using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Threading;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor;
using Consolonia.Core.Dummy;
using Consolonia.Core.Text;
using TextShaper = Consolonia.Core.Text.TextShaper;

namespace Consolonia.Core.Infrastructure
{
    public class ConsoloniaPlatform : IWindowingPlatform
    {
        internal static ConsoloniaPlatformSettings Settings =>
            AvaloniaLocator.Current.GetService<IPlatformSettings>() as ConsoloniaPlatformSettings;

        public IWindowImpl CreateWindow()
        {
            return new ConsoleWindowImpl();
        }

        public IWindowImpl CreateEmbeddableWindow()
        {
            return RaiseNotSupported<IWindowImpl>(NotSupportedRequestCode.CreateEmbeddableWindow);
        }

        public ITrayIconImpl CreateTrayIcon()
        {
            throw new NotImplementedException();
        }

        public ITopLevelImpl CreateEmbeddableTopLevel()
        {
            throw new NotImplementedException();
        }

        public void Initialize()
        {
            NotSupported += InternalWorkaroundsIgnore;
            NotSupported += KeyInputIgnore;
            NotSupported += RenderNotSupportedIgnore;

            AvaloniaLocator.CurrentMutable.BindToSelf(this)
                .Bind<IWindowingPlatform>().ToConstant(this)
                /*todo: need replacement? .Bind<IPlatformThreadingInterface>().ToSingleton<ConsoloniaPlatformThreadingInterface>()*/
                .Bind<IRenderTimer>().ToConstant(new UiThreadRenderTimer(120))
                .Bind<IDispatcherImpl>().ToConstant(new ManagedDispatcherImpl(null))
                /*.Bind<IRenderTimer>().ToConstant(new SleepLoopRenderTimer(120))*/
                /*SleepLoopRenderTimer : IRenderTimer*/
                /*.Bind<IRenderLoop>().ToConstant(new RenderLoop()) todo: is internal now*/
                .Bind<PlatformHotkeyConfiguration>().ToConstant(new PlatformHotkeyConfiguration(KeyModifiers.Control))
                .Bind<IKeyboardDevice>().ToConstant(new ConsoleKeyboardDevice())
                .Bind<IFontManagerImpl>().ToConstant(new FontManagerImpl())
                .Bind<ITextShaperImpl>().ToConstant(new TextShaper())
                .Bind<IMouseDevice>().ToConstant(new MouseDevice())
                .Bind<ICursorFactory>().ToConstant(new ConsoleCursorFactory())
                .Bind<IPlatformIconLoader>().ToConstant(new DummyIconLoader())
                .Bind<IPlatformSettings>().ToConstant(new ConsoloniaPlatformSettings
                {
                    UnsafeRendering = true,
                    UnsafeInput = true
                })
                // .Bind<IStorageProvider>().ToSingleton<BclStorageProvider>()
                .Bind<IRuntimePlatform>().ToConstant(new StandardRuntimePlatform())
                //.Bind<IClipboard>().ToConstant(null)
                /*.Bind<IKeyboardNavigationHandler>().ToTransient<ArrowsAndKeyboardNavigationHandler>() todo: implement this navigation*/
                //.Bind<IClipboard>().ToConstant(new X11Clipboard(this))
                //.Bind<IPlatformSettings>().ToConstant(new PlatformSettingsStub())
                //.Bind<ISystemDialogImpl>().ToConstant(new GtkSystemDialog())
                /*.Bind<IMountedVolumeInfoProvider>().ToConstant(new LinuxMountedVolumeInfoProvider())*/;
        }

        [DebuggerStepThrough]
        internal static TResult RaiseNotSupported<TResult>(NotSupportedRequestCode errorCode,
            params object[] information)
        {
            var notSupportedRequest = new NotSupportedRequest(errorCode, information);
            NotSupported?.Invoke(notSupportedRequest);
            notSupportedRequest.CheckHandled(typeof(TResult));
            try
            {
                return (TResult)notSupportedRequest.Result;
            }
            catch (InvalidCastException exception)
            {
                throw new InvalidOperationException(
                    $"The result of the NotSupportedRequest with code {errorCode} must be of type {typeof(TResult).FullName}.",
                    exception);
            }
        }

        internal static void RaiseNotSupported(NotSupportedRequestCode errorCode, params object[] information)
        {
            RaiseNotSupported<object>(errorCode, information);
        }

        public static event Action<NotSupportedRequest> NotSupported;

        /// <summary>
        ///     Something we have to skip as a workaround
        /// </summary>
        private static void InternalWorkaroundsIgnore(NotSupportedRequest notSupportedRequest)
        {
            switch (notSupportedRequest.ErrorCode)
            {
                case NotSupportedRequestCode.OverlayPopupHostRender
                    when ReferenceEquals(notSupportedRequest.Information[0], Brushes.White) &&
                         notSupportedRequest.Information[1] is null &&
                         ((RoundedRect)notSupportedRequest.Information[2]).Rect.TopLeft is { X: 0, Y: 0 }:
                    notSupportedRequest.SetHandled();
                    break;
            }
        }

        private static void RenderNotSupportedIgnore(NotSupportedRequest notSupportedRequest)
        {
            if (!Settings.UnsafeRendering)
                return;

            switch (notSupportedRequest.ErrorCode)
            {
                case NotSupportedRequestCode.ColorFromBrushPosition:
                    notSupportedRequest.SetHandled(Colors.Black);
                    break;
                case NotSupportedRequestCode.BackgroundWasNotColoredWhileMapping:
                    var color = (Color)notSupportedRequest.Information[1];
                    notSupportedRequest.SetHandled(
                        ((EgaConsoleColorMode)notSupportedRequest.Information[0]).MapColors(
                            Color.FromRgb(color.R, color.G, color.B),
                            (Color)notSupportedRequest.Information[2],
                            (FontWeight?)notSupportedRequest.Information[3]));
                    break;
                case NotSupportedRequestCode.DrawGlyphRunNotSupported:
                    notSupportedRequest.SetHandled();
                    break;


                case NotSupportedRequestCode.DrawStringWithNonSolidColorBrush:
                    notSupportedRequest.SetHandled(Brushes.Black);
                    break;
                case NotSupportedRequestCode.ExtractColorFromPenNotSupported:
                    notSupportedRequest.SetHandled((Colors.Black, new LineStyles
                    {
                        Bottom = LineStyle.SingleLine,
                        Left = LineStyle.SingleLine,
                        Right = LineStyle.SingleLine,
                        Top = LineStyle.SingleLine
                    }));
                    break;
                case NotSupportedRequestCode.TransformLineWithRotationNotSupported:
                    notSupportedRequest.SetHandled(notSupportedRequest.Information[1]);
                    break;
                case NotSupportedRequestCode.BitmapsNotSupported:
                    notSupportedRequest.SetHandled(new DummyBitmap());
                    break;
                case NotSupportedRequestCode.PushClipWithRoundedRectNotSupported:
                case NotSupportedRequestCode.PushOpacityNotSupported:
                case NotSupportedRequestCode.DrawingRoundedOrNonUniformRectandle:
                case NotSupportedRequestCode.DrawingBoxShadowNotSupported:
                case NotSupportedRequestCode.DrawGeometryNotSupported:
                case NotSupportedRequestCode.DrawEllipseNotSupported:
                case NotSupportedRequestCode.PushClipRegionNotSupported:
                case NotSupportedRequestCode.PushLayerNotSupported:
                case NotSupportedRequestCode.DrawRegionNotSupported:
                case NotSupportedRequestCode.CubicBezierToNotSupported:
                case NotSupportedRequestCode.QuadraticBezierToNotSupported:
                case NotSupportedRequestCode.VerticalTextDecorationNotSupported:
                    notSupportedRequest.SetHandled();
                    break;
                // console window methods that are not implemented by design, because they are not applicable to console windows
                case NotSupportedRequestCode.ConsoleWindowCreateTrayIconNotSupported:
                case NotSupportedRequestCode.ConsoleWindowCreateEmbeddableTopLevelNotSupported:
                case NotSupportedRequestCode.ConsoleWindowHideNotSupported:
                case NotSupportedRequestCode.ConsoleWindowActivateNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetTopmostNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetParentNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetEnabledNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetSystemDecorationsNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetIconNotSupported:
                case NotSupportedRequestCode.ConsoleWindowShowTaskbarIconNotSupported:
                case NotSupportedRequestCode.ConsoleWindowCanResizeNotSupported:
                case NotSupportedRequestCode.ConsoleWindowBeginMoveDragNotSupported:
                case NotSupportedRequestCode.ConsoleWindowBeginResizeDragNotSupported:
                case NotSupportedRequestCode.ConsoleWindowBeginResizeNotSupported:
                case NotSupportedRequestCode.ConsoleWindowMoveNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetMinMaxSizeNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetExtendClientAreaToDecorationsHintNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetExtendClientAreaChromeHintsNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetExtendClientAreaTitleBarHeightHintNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetCanMinimizeNotSupported:
                case NotSupportedRequestCode.ConsoleWindowSetCanMaximizeNotSupported:
                    notSupportedRequest.SetHandled();
                    break;
            }
        }

        /// <summary>
        ///     Ignore key input requests that are not supported.
        /// </summary>
        /// <param name="notSupportedRequest"></param>
        /// <exception cref="NotImplementedException"></exception>
        private static void KeyInputIgnore(NotSupportedRequest notSupportedRequest)
        {
            if (!Settings.UnsafeInput)
                return;

            switch (notSupportedRequest.ErrorCode)
            {
                case NotSupportedRequestCode.InputNotSupported:
                    notSupportedRequest.SetHandled(Key.None);
                    break;
            }
        }
    }
}