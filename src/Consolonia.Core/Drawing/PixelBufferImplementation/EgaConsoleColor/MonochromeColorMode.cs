using System;
using Avalonia.Media;
using Consolonia.Core.InternalHelpers;

namespace Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor
{
    public class MonochromeColorMode : IConsoleColorMode
    {
        public Color Blend(Color color1, Color color2, bool isTargetForeground)
        {
            return !isTargetForeground ? Colors.Black : color2;
        }

        public (object background, object foreground) MapColors(Color background, Color foreground, FontWeight? weight)
        {
            return (MapColorInternal(background), MapColorInternal(foreground));
        }

        private static object MapColorInternal(Color color)
        {
            if (CommonInternalHelper.IsNearlyEqual(color.A, 0) || CommonInternalHelper.IsNearlyEqual(color.R, 0)
                && CommonInternalHelper.IsNearlyEqual(color.G, 0)
                && CommonInternalHelper.IsNearlyEqual(color.B, 0))
                return ConsoleColor.Black;
            return ConsoleColor.White;
        }
    }
}