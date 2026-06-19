using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Core.Text.Fonts;

namespace Consolonia.Core.Text
{
    internal class GlyphRunImpl : IGlyphRunImpl
    {
        public GlyphRunImpl(GlyphTypeface glyphTypeface, double fontRenderingEmSize,
            IReadOnlyList<GlyphInfo> glyphInfos, Point baselineOrigin)
        {
            FontRenderingEmSize = fontRenderingEmSize;
            GlyphTypeface = glyphTypeface;
            ConsoleTypeface = (glyphTypeface.PlatformTypeface as ConsolePlatformTypeface)?.ConsoleTypeface;
            BaselineOrigin = baselineOrigin;
            GlyphInfos = glyphInfos;
            FontMetrics metrics = ConsoleTypeface?.Metrics ?? glyphTypeface.Metrics;
            double scale = metrics.DesignEmHeight != 0
                ? fontRenderingEmSize / metrics.DesignEmHeight
                : 1;
            double width = glyphInfos.Sum(gi => gi.GlyphAdvance) * scale;
            Bounds = new Rect(new Point(0, 0), new Size(width, fontRenderingEmSize));
        }

        public IReadOnlyList<GlyphInfo> GlyphInfos { get; }

        public void Dispose()
        {
        }

        public IReadOnlyList<float> GetIntersections(float lowerLimit, float upperLimit)
        {
            // empty intersections defaults to entire span.
            return new List<float>();
        }

        public GlyphTypeface GlyphTypeface { get; }

        public IConsoleTypeface ConsoleTypeface { get; }

        public double FontRenderingEmSize { get; }
        public Point BaselineOrigin { get; }
        public Rect Bounds { get; }
    }
}