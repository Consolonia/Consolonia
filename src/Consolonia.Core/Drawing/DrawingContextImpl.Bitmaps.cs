//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Dummy;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Bitmap - drawing implementation
    /// </summary>
    internal partial class DrawingContextImpl
    {
        private BitmapRenderer _bitmapRenderer;

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

            var renderInterface = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();

            _bitmapRenderer ??= CreateBitmapRenderer();
            _bitmapRenderer.Draw(source, renderInterface, targetRect, intersectedRect, GetBitmapInterpolationMode());
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

        /// <summary>
        ///     Picks the best bitmap renderer the terminal is capable of (capabilities are detected in PrepareConsole).
        /// </summary>
        private BitmapRenderer CreateBitmapRenderer()
        {
            ConsoleCapabilities capabilities = ConsoleWindowImpl.Console.Capabilities;

            if (capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics) &&
                AvaloniaLocator.Current.GetService<IConsoleColorMode>() is RgbConsoleColorMode)
                return new KittyBitmapRenderer(this);

            if (capabilities.HasFlag(ConsoleCapabilities.SupportsSixel))
                return new SixelBitmapRenderer(this);

            return new QuadPixelBitmapRenderer(this);
        }
    }
}