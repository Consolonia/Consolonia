//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.InternalHelpers;
using Consolonia.Core.Text;
using Consolonia.Core.Text.Fonts;

namespace Consolonia.Core.Drawing
{
    internal partial class DrawingContextImpl : IDrawingContextImpl
    {
        private readonly Stack<PixelRect> _clipStack = new(100);
        private readonly ConsoleWindowImpl _consoleWindowImpl;
        private readonly PixelBuffer _pixelBuffer;
        private readonly Matrix _postTransform = Matrix.Identity;

        // ReSharper disable once CollectionNeverQueried.Local
        private readonly Stack<RenderOptions> _renderOptions = new();
        private Matrix _transform = Matrix.Identity;

        public DrawingContextImpl(ConsoleWindowImpl consoleWindowImpl)
        {
            _consoleWindowImpl = consoleWindowImpl;
            _pixelBuffer = consoleWindowImpl.PixelBuffer;
            _clipStack.Push(_pixelBuffer.Size);
        }

        private PixelRect CurrentClip => _clipStack.Peek();

        public void Dispose()
        {
        }

        public void Clear(Color color)
        {
            // todo: try to throw an exception here, there will be an exception in logger
            /*if (color != Colors.Transparent)
            {
                ConsoloniaPlatform.RaiseNotSupported(1);
                return;
            }

            _pixelBuffer.Foreach((_, _, _) =>
                new Pixel(PixelBackground.Default));*/
        }

        public void DrawEllipse(IBrush brush, IPen pen, Rect rect)
        {
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.DrawEllipseNotSupported);
        }

        public void DrawGlyphRun(IBrush foreground, IGlyphRunImpl glyphRun)
        {
            if (glyphRun.FontRenderingEmSize.IsNearlyEqual(0)) return;

            if (glyphRun is not GlyphRunImpl glyphRunImpl)
            {
                glyphRunImpl = ConsoloniaPlatform.RaiseNotSupported<GlyphRunImpl>(
                    NotSupportedRequestCode.DrawGlyphRunNotSupported, this, foreground, glyphRun);
                if (glyphRunImpl == null)
                    return;
            }

            if (foreground is not ISolidColorBrush solidColorBrush)
            {
                solidColorBrush = ConsoloniaPlatform.RaiseNotSupported<ISolidColorBrush>(
                    NotSupportedRequestCode.DrawStringWithNonSolidColorBrush, this, foreground);

                if (solidColorBrush == null)
                    return;
            }

            IGlyphRunRender glyphTypefaceRender = glyphRunImpl.ConsoleTypeface;

            Color foregroundColor = solidColorBrush.Color;
            var startPosition = new Point().Transform(Transform).ToPixelPoint();
            glyphTypefaceRender.DrawGlyphRun(this, startPosition, glyphRunImpl, foregroundColor,
                out PixelRect rectToRefresh);

            _consoleWindowImpl.DirtyRegions.AddRect(rectToRefresh);
        }

        public IDrawingContextLayerImpl CreateLayer(PixelSize size)
        {
            return new RenderTarget(_consoleWindowImpl);
        }

        public void PushClip(Rect clip)
        {
            clip = new Rect(clip.Position.Transform(Transform), clip.BottomRight.Transform(Transform));
            _clipStack.Push(CurrentClip.Intersect(clip.ToPixelRect()));
        }

        public void PushClip(RoundedRect clip)
        {
            if (clip.IsRounded)
                ConsoloniaPlatform.RaiseNotSupported(
                    NotSupportedRequestCode.PushClipWithRoundedRectNotSupported, this, clip);

            PushClip(clip.Rect);
        }

        public void PushClip(IPlatformRenderInterfaceRegion region)
        {
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.PushClipRegionNotSupported);

            // we need to keep clipstack aligned even if this is an approximation.
            PushClip(new Rect(region.Bounds.Left,
                region.Bounds.Top,
                region.Bounds.Right - region.Bounds.Left,
                region.Bounds.Bottom - region.Bounds.Top));
        }

        public void PopClip()
        {
            _clipStack.Pop();
        }

        public void PushOpacity(double opacity, Rect? bounds)
        {
            if (opacity.IsNearlyEqual(1)) return;
            ConsoloniaPlatform.RaiseNotSupported(
                NotSupportedRequestCode.PushOpacityNotSupported, this, opacity, bounds);
        }

        public void PopOpacity()
        {
            ConsoloniaPlatform.RaiseNotSupported(
                NotSupportedRequestCode.PushOpacityNotSupported, this);
        }

        public void PushOpacityMask(IBrush mask, Rect bounds)
        {
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.PushOpacityNotSupported);
        }

        public void PopOpacityMask()
        {
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.PushOpacityNotSupported);
        }

        public void PushGeometryClip(IGeometryImpl clip)
        {
            // this is an approximation, we just use the bounds
            PushClip(clip.Bounds);
        }

        public void PopGeometryClip()
        {
            PopClip();
        }

        public void PushRenderOptions(RenderOptions renderOptions)
        {
            _renderOptions.Push(renderOptions);
        }

        public void PopRenderOptions()
        {
            _renderOptions.Pop();
        }

        public void PushTextOptions(TextOptions textOptions)
        {
            // Avalonia pushes text options around each text draw. Text is already shaped into glyph runs here.
        }

        public void PopTextOptions()
        {
        }

        public object GetFeature(Type t)
        {
            throw new NotImplementedException();
        }

        public Matrix Transform
        {
            get => _transform;
            set => _transform = value * _postTransform;
        }

        public void DrawRegion(IBrush brush, IPen pen, IPlatformRenderInterfaceRegion region)
        {
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.DrawRegionNotSupported);
        }

        public void PushLayer(Rect bounds)
        {
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.PushLayerNotSupported);
        }

        public void PopLayer()
        {
            ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.PushLayerNotSupported);
        }


        public void DrawPixel(Pixel pixel, PixelPoint position)
        {
            if (position.Y < CurrentClip.Y || position.Y >= CurrentClip.Bottom) return;

            if (CurrentClip.ContainsExclusive(position))
                _pixelBuffer[position] = _pixelBuffer[position].Blend(pixel);

            if (pixel.Width == 1)
                return;

            int rightLimit = int.Min(CurrentClip.Right, position.X + pixel.Width) - 1;

            int leftLimit = int.Max(CurrentClip.X, position.X + 1);

            for (int x = leftLimit; x <= rightLimit; x++)
            {
                PixelPoint positionInSequence = position.WithX(x);
                Pixel blendedPixel = _pixelBuffer[positionInSequence]
                    .Blend(new Pixel(PixelForeground.Empty, pixel.Background));

                _pixelBuffer[positionInSequence] = new Pixel(PixelForeground.Empty, blendedPixel.Background);
            }
        }
    }
}
