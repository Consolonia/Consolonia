using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Sixel framebuffer render interface. Delegates everything to the Skia fallback
    ///     except CreateBackendContext, which returns a SixelPlatformRenderInterfaceContext.
    /// </summary>
    internal class SixelRenderInterface : IPlatformRenderInterface
    {
        private readonly IPlatformRenderInterface _fallback;

        internal SixelRenderInterface(IPlatformRenderInterface fallback)
        {
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        public IGeometryImpl CreateEllipseGeometry(Rect rect)
            => _fallback.CreateEllipseGeometry(rect);

        public IGeometryImpl CreateLineGeometry(Point p1, Point p2)
            => _fallback.CreateLineGeometry(p1, p2);

        public IGeometryImpl CreateRectangleGeometry(Rect rect)
            => _fallback.CreateRectangleGeometry(rect);

        public IStreamGeometryImpl CreateStreamGeometry()
            => _fallback.CreateStreamGeometry();

        public IGeometryImpl CreateGeometryGroup(FillRule fillRule, IReadOnlyList<IGeometryImpl> children)
            => _fallback.CreateGeometryGroup(fillRule, children);

        public IGeometryImpl CreateCombinedGeometry(GeometryCombineMode combineMode, IGeometryImpl g1, IGeometryImpl g2)
            => _fallback.CreateCombinedGeometry(combineMode, g1, g2);

        public IGeometryImpl BuildGlyphRunGeometry(GlyphRun glyphRun)
            => _fallback.BuildGlyphRunGeometry(glyphRun);

        public IRenderTargetBitmapImpl CreateRenderTargetBitmap(PixelSize size, Vector dpi)
            => _fallback.CreateRenderTargetBitmap(size, dpi);

        public IWriteableBitmapImpl CreateWriteableBitmap(PixelSize size, Vector dpi, PixelFormat format, AlphaFormat alphaFormat)
            => _fallback.CreateWriteableBitmap(size, dpi, format, alphaFormat);

        public IBitmapImpl LoadBitmap(string fileName)
            => _fallback.LoadBitmap(fileName);

        public IBitmapImpl LoadBitmap(Stream stream)
            => _fallback.LoadBitmap(stream);

        public IWriteableBitmapImpl LoadWriteableBitmapToHeight(Stream stream, int height, BitmapInterpolationMode interpolationMode)
            => _fallback.LoadWriteableBitmapToHeight(stream, height, interpolationMode);

        public IWriteableBitmapImpl LoadWriteableBitmapToWidth(Stream stream, int width, BitmapInterpolationMode interpolationMode)
            => _fallback.LoadWriteableBitmapToWidth(stream, width, interpolationMode);

        public IWriteableBitmapImpl LoadWriteableBitmap(string fileName)
            => _fallback.LoadWriteableBitmap(fileName);

        public IWriteableBitmapImpl LoadWriteableBitmap(Stream stream)
            => _fallback.LoadWriteableBitmap(stream);

        public IBitmapImpl LoadBitmapToWidth(Stream stream, int width, BitmapInterpolationMode interpolationMode)
            => _fallback.LoadBitmapToWidth(stream, width, interpolationMode);

        public IBitmapImpl LoadBitmapToHeight(Stream stream, int height, BitmapInterpolationMode interpolationMode)
            => _fallback.LoadBitmapToHeight(stream, height, interpolationMode);

        public IBitmapImpl ResizeBitmap(IBitmapImpl bitmapImpl, PixelSize destinationSize, BitmapInterpolationMode interpolationMode)
            => _fallback.ResizeBitmap(bitmapImpl, destinationSize, interpolationMode);

        public IBitmapImpl LoadBitmap(PixelFormat format, AlphaFormat alphaFormat, IntPtr data, PixelSize size, Vector dpi, int stride)
            => _fallback.LoadBitmap(format, alphaFormat, data, size, dpi, stride);

        public IGlyphRunImpl CreateGlyphRun(IGlyphTypeface glyphTypeface, double fontRenderingEmSize, IReadOnlyList<GlyphInfo> glyphInfos, Point baselineOrigin)
            => _fallback.CreateGlyphRun(glyphTypeface, fontRenderingEmSize, glyphInfos, baselineOrigin);

        public IPlatformRenderInterfaceContext CreateBackendContext(IPlatformGraphicsContext graphicsApiContext)
        {
            return new SixelPlatformRenderInterfaceContext(_fallback);
        }

        public bool IsSupportedBitmapPixelFormat(PixelFormat format)
            => _fallback.IsSupportedBitmapPixelFormat(format);

        public IPlatformRenderInterfaceRegion CreateRegion()
            => _fallback.CreateRegion();

        public bool SupportsIndividualRoundRects => _fallback.SupportsIndividualRoundRects;

        public AlphaFormat DefaultAlphaFormat => _fallback.DefaultAlphaFormat;

        public PixelFormat DefaultPixelFormat => _fallback.DefaultPixelFormat;

        public bool SupportsRegions => _fallback.SupportsRegions;
    }
}
