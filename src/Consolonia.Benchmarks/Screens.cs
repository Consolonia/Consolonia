using Avalonia.Media;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Benchmarks
{
    /// <summary>The screens and frames the output benchmarks share, so their text baselines match.</summary>
    internal static class Screens
    {
        /// <summary>A screen of text. Each run of eight cells shares one color, like typical UI text.</summary>
        public static PixelBuffer Text(ushort cellsWide, ushort cellsHigh)
        {
            var text = new PixelBuffer(cellsWide, cellsHigh);
            for (int i = 0; i < cellsWide * cellsHigh; i++)
            {
                int run = i / 8;
                Color foreground = Color.FromRgb((byte)(run * 37), (byte)(run * 11), 200);
                Color background = Color.FromRgb(20, (byte)(run * 5), (byte)(run * 3));
                text[(ushort)(i % cellsWide), (ushort)(i / cellsWide)] = new Pixel(
                    new PixelForeground(new Symbol((char)('A' + i % 26)), foreground),
                    new PixelBackground(background));
            }

            return text;
        }

        /// <summary>Writes one frame in which every cell of <paramref name="screen" /> changed.</summary>
        public static void WholeScreen(AnsiConsoleOutput output, PixelBuffer screen)
        {
            output.BeginUpdate();
            for (ushort y = 0; y < screen.Height; y++)
            for (ushort x = 0; x < screen.Width; x++)
                output.WritePixel(new PixelBufferCoordinate(x, y), screen[x, y]);
            output.EndUpdate(screen);
        }
    }
}
