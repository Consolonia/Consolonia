using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Wraps the Skia drawing context and applies a cell-to-pixel scale transform
    ///     so that Avalonia's cell-unit layout coordinates map to pixel coordinates.
    /// </summary>
    internal class SixelDrawingContext : IDrawingContextImpl
    {
        internal readonly IDrawingContextImpl Inner;
        private readonly SixelRenderTarget _renderTarget;

        public SixelDrawingContext(IDrawingContextImpl inner, SixelRenderTarget renderTarget = null)
        {
            Inner = inner;
            _renderTarget = renderTarget;
        }

        public Matrix Transform
        {
            get => Inner.Transform;
            set => Inner.Transform = value;
        }

        public void Clear(Color color) => Inner.Clear(color);

        public void DrawBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
            => Inner.DrawBitmap(source, opacity, sourceRect, destRect);

        public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
            => Inner.DrawBitmap(source, opacityMask, opacityMaskRect, destRect);

        public void DrawEllipse(IBrush brush, IPen pen, Rect rect)
            => Inner.DrawEllipse(brush, pen, rect);

        public void DrawGlyphRun(IBrush foreground, IGlyphRunImpl glyphRun)
            => Inner.DrawGlyphRun(foreground, glyphRun);

        public void DrawLine(IPen pen, Point p1, Point p2)
            => Inner.DrawLine(pen, p1, p2);

        public void DrawGeometry(IBrush brush, IPen pen, IGeometryImpl geometry)
            => Inner.DrawGeometry(brush, pen, geometry);

        public void DrawRectangle(IBrush brush, IPen pen, RoundedRect rrect, BoxShadows boxShadows = default)
            => Inner.DrawRectangle(brush, pen, rrect, boxShadows);

        public void DrawRegion(IBrush brush, IPen pen, IPlatformRenderInterfaceRegion region)
            => Inner.DrawRegion(brush, pen, region);

        public IDrawingContextLayerImpl CreateLayer(PixelSize size)
            => new SixelLayerWrapper(Inner.CreateLayer(size));

        public void PushClip(Rect clip) => Inner.PushClip(clip);

        public void PushClip(RoundedRect clip) => Inner.PushClip(clip);

        public void PushClip(IPlatformRenderInterfaceRegion region) => Inner.PushClip(region);

        public void PopClip() => Inner.PopClip();

        public void PushOpacity(double opacity, Rect? bounds) => Inner.PushOpacity(opacity, bounds);

        public void PopOpacity() => Inner.PopOpacity();

        public void PushOpacityMask(IBrush mask, Rect bounds) => Inner.PushOpacityMask(mask, bounds);

        public void PopOpacityMask() => Inner.PopOpacityMask();

        public void PushGeometryClip(IGeometryImpl clip) => Inner.PushGeometryClip(clip);

        public void PopGeometryClip() => Inner.PopGeometryClip();

        public void PushRenderOptions(RenderOptions renderOptions) => Inner.PushRenderOptions(renderOptions);

        public void PopRenderOptions() => Inner.PopRenderOptions();

        public void PushLayer(Rect bounds) => Inner.PushLayer(bounds);

        public void PopLayer() => Inner.PopLayer();

        public object GetFeature(Type t) => Inner.GetFeature(t);

        public void Dispose()
        {
            Inner.Dispose();
            // After the frame's drawing context is closed, output the rendered bitmap as Sixel
            _renderTarget?.RenderToDevice();
        }
    }

    /// <summary>
    ///     Wraps a Skia layer so that Blit() unwraps SixelDrawingContext before
    ///     delegating to the inner Skia layer (which expects its own DrawingContextImpl).
    /// </summary>
    internal class SixelLayerWrapper : IDrawingContextLayerImpl
    {
        private readonly IDrawingContextLayerImpl _inner;

        public SixelLayerWrapper(IDrawingContextLayerImpl inner)
        {
            _inner = inner;
        }

        public void Blit(IDrawingContextImpl context)
        {
            // Unwrap our SixelDrawingContext to get the Skia context that Skia expects
            IDrawingContextImpl actual = context is SixelDrawingContext sixel ? sixel.Inner : context;
            _inner.Blit(actual);
        }

        public bool CanBlit => _inner.CanBlit;

        public IDrawingContextImpl CreateDrawingContext(bool useScaledDrawing)
            => _inner.CreateDrawingContext(useScaledDrawing);

        public bool IsCorrupted => _inner.IsCorrupted;

        public Vector Dpi => _inner.Dpi;

        public PixelSize PixelSize => _inner.PixelSize;

        public int Version => _inner.Version;

        public void Save(string fileName, int? quality = null) => _inner.Save(fileName, quality);

        public void Save(Stream stream, int? quality = null) => _inner.Save(stream, quality);

        public void Dispose() => _inner.Dispose();
    }
}
