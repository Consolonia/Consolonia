using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Platform;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Wraps the Skia backend context. Creates SixelRenderTarget instances that
    ///     render to an offscreen Skia bitmap and output via Sixel.
    /// </summary>
    internal sealed class SixelPlatformRenderInterfaceContext : IPlatformRenderInterfaceContext
    {
        private readonly IPlatformRenderInterface _fallbackRenderInterface;
        private readonly IPlatformRenderInterfaceContext _fallbackContext;

        public SixelPlatformRenderInterfaceContext(IPlatformRenderInterface fallbackRenderInterface)
        {
            _fallbackRenderInterface = fallbackRenderInterface;
            _fallbackContext = fallbackRenderInterface.CreateBackendContext(null);
        }

        public IRenderTarget CreateRenderTarget(IEnumerable<object> surfaces)
        {
            var consoleWindow = surfaces.OfType<ConsoleWindowImpl>().Single();
            var console = AvaloniaLocator.Current.GetRequiredService<IConsoleOutput>();

            // ClientSize is already in pixels when SixelMode is active
            var pixelSize = new PixelSize((int)consoleWindow.ClientSize.Width, (int)consoleWindow.ClientSize.Height);
            IDrawingContextLayerImpl offscreenTarget = _fallbackContext.CreateOffscreenRenderTarget(pixelSize, 1.0);

            return new SixelRenderTarget(consoleWindow, offscreenTarget, console, _fallbackRenderInterface, _fallbackContext,
                console.CellPixelWidth, console.CellPixelHeight);
        }

        public IDrawingContextLayerImpl CreateOffscreenRenderTarget(PixelSize pixelSize, double scaling)
        {
            return _fallbackContext.CreateOffscreenRenderTarget(pixelSize, scaling);
        }

        public object TryGetFeature(Type featureType)
        {
            return _fallbackContext.TryGetFeature(featureType);
        }

        public bool IsLost => _fallbackContext.IsLost;

        public IReadOnlyDictionary<Type, object> PublicFeatures => _fallbackContext.PublicFeatures;

        public void Dispose()
        {
            _fallbackContext.Dispose();
        }
    }
}
