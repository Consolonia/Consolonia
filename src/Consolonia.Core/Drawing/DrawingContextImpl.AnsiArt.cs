using Avalonia;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    internal partial class DrawingContextImpl
    {
        private void DrawPixelBufferBitmap(PixelBufferBitmapImpl pixelBufferBitmap, Rect sourceRect,
            Rect destRect)
        {
            var targetRect = new Rect(Transform.Transform(destRect.TopLeft),
                    Transform.Transform(destRect.BottomRight))
                .ToPixelRect();

            PixelRect intersectedRect = CurrentClip.Intersect(targetRect);

            if (intersectedRect.IsEmpty())
                return;

            for (int py = intersectedRect.Y; py < intersectedRect.Bottom; py++)
            for (int px = intersectedRect.X; px < intersectedRect.Right; px++)
            {
                double relativeX = (double)(px - targetRect.X) / targetRect.Width;
                double relativeY = (double)(py - targetRect.Y) / targetRect.Height;

                int ax = (int)(sourceRect.X + relativeX * sourceRect.Width);
                int ay = (int)(sourceRect.Y + relativeY * sourceRect.Height);

                //todo: low this is bad default scaling
                if (ax >= 0 && ax < pixelBufferBitmap.Buffer.Width && ay >= 0 &&
                    ay < pixelBufferBitmap.Buffer.Height)
                {
                    // todo: handle opacity if necessary
                    _pixelBuffer.Blend(new PixelPoint(px, py), in pixelBufferBitmap.Buffer.CellAt(ax, ay));
                }
            }

            ConsoleWindowImpl.DirtyRegions.AddRect(intersectedRect);
        }
    }
}