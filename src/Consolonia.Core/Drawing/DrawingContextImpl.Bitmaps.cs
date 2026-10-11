using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Dummy;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Bitmap - drawing implementation
    /// </summary>
    internal partial class DrawingContextImpl
    {
        private BitmapRenderer _bitmapRenderer;
        private IPlatformRenderInterface _renderInterface;

        public void DrawBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
        {
            switch (source)
            {
                case DummyBitmap:
                    return;
                case PixelBufferBitmapImpl pixelBufferBitmap:
                    DrawPixelBufferBitmap(pixelBufferBitmap, sourceRect, destRect);
                    return;
            }

            var targetRect = new Rect(Transform.Transform(destRect.TopLeft),
                    Transform.Transform(destRect.BottomRight))
                .ToPixelRect();

            PixelRect intersectedRect = CurrentClip.Intersect(targetRect);

            if (intersectedRect.IsEmpty())
                return;

            _renderInterface ??= AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();
            // the render target's renderer, kept for the terminal; a context without one (tests) makes its own
            _bitmapRenderer ??= _renderTarget?.BitmapRenderer ?? BitmapRenderer.Create(ConsoleWindowImpl.Console);
            _bitmapRenderer.Draw(this, source, _renderInterface, targetRect, intersectedRect,
                GetBitmapInterpolationMode());
        }

        /// <summary>
        ///     The interpolation the innermost render options ask for (RenderOptions.BitmapInterpolationMode, so
        ///     an Image can keep its pixels hard-edged when enlarged), or medium quality when none do.
        /// </summary>
        private BitmapInterpolationMode GetBitmapInterpolationMode()
        {
            foreach (RenderOptions renderOptions in _renderOptions)
                if (renderOptions.BitmapInterpolationMode != BitmapInterpolationMode.Unspecified)
                    return renderOptions.BitmapInterpolationMode;

            return BitmapInterpolationMode.MediumQuality;
        }

        public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
        {
            throw new NotImplementedException();
        }
    }
}