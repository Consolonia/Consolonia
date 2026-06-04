using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    internal sealed class ConsoloniaPlatformRenderInterfaceContext : IPlatformRenderInterfaceContext
    {
        public object TryGetFeature(Type featureType)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }

        public IRenderTarget CreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces)
        {
            if (!HasSingleConsoleWindowSurface(surfaces))
                throw new ArgumentException(
                    $"{nameof(RenderTarget)} requires exactly one {nameof(ConsoleWindowImpl)} surface.",
                    nameof(surfaces));

            return new RenderTarget(surfaces);
        }

        public IDrawingContextLayerImpl CreateOffscreenRenderTarget(PixelSize pixelSize, Vector dpi,
            bool mono)
        {
            throw new NotImplementedException();
        }

        public bool IsLost => false;

        public IReadOnlyDictionary<Type, object> PublicFeatures { get; } = new Dictionary<Type, object>();

        public PixelSize? MaxOffscreenRenderTargetPixelSize => null;

        public bool IsReadyToCreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces)
        {
            return HasSingleConsoleWindowSurface(surfaces);
        }

        private static bool HasSingleConsoleWindowSurface(IEnumerable<IPlatformRenderSurface> surfaces)
        {
            if (surfaces == null)
                return false;

            return surfaces.OfType<ConsoleWindowImpl>().Take(2).Count() == 1;
        }
    }
}
