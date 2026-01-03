using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    public interface IConsoleColorMode
    {
        /// <summary>
        ///     Blend two colors
        /// </summary>
        /// <param name="color1">target/under color</param>
        /// <param name="color2">source/above color</param>
        /// <param name="isTargetForeground">whether <see cref="color1" /> is foreground</param>
        Color Blend(Color color1, Color color2, bool isTargetForeground);

        /// <summary>
        ///     Map standard avalonia colors to native platform colors supported by current color mode
        /// </summary>
        (object background, object foreground) MapColors(Color background, Color foreground, FontWeight? weight);//todo: boxing of returned values!!
    }
}