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
        private BitmapConverter _bitmapRenderer;
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
            // Use the render target's converter, which lives as long as the terminal does.
            // A context without a render target (for example in tests) creates its own.
            _bitmapRenderer ??= _renderTarget?.BitmapConverter ?? BitmapConverter.Create(ConsoleWindowImpl.Console);
            _bitmapRenderer.Draw(this, source, _renderInterface, targetRect, intersectedRect,
                GetBitmapInterpolationMode());
        }

        public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Returns the bitmap interpolation mode from the innermost render options that set one, or
        ///     medium quality if none do. This lets an Image set RenderOptions.BitmapInterpolationMode to keep
        ///     hard pixel edges when it is enlarged.
        /// </summary>
        private BitmapInterpolationMode GetBitmapInterpolationMode()
        {
            foreach (RenderOptions renderOptions in _renderOptions)
                if (renderOptions.BitmapInterpolationMode != BitmapInterpolationMode.Unspecified)
                    return renderOptions.BitmapInterpolationMode;

            return BitmapInterpolationMode.MediumQuality;
        }
    }
}