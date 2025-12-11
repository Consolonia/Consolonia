using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers;

namespace Consolonia.Core.Text.Fonts
{
    /// <summary>
    ///     This represents a psuedo-typeface for console rendering.
    /// </summary>
    public sealed class ConsoleTypeface : IGlyphTypeface, ITextShaperImpl, IGlyphRunRender
    {
        private static readonly object GlyphCacheSync = new();
        private static readonly Dictionary<ushort, string> GlyphTextByIndex = new();
        private static readonly Dictionary<ushort, ushort> GlyphWidthByIndex = new();
        private static readonly Dictionary<string, ushort> GlyphIndexByText = new();

        private static readonly FontMetrics MetricsSingleton = new()
        {
            // https://docs.microsoft.com/en-us/typography/opentype/spec/ttch01#funits-and-the-em-square
            DesignEmHeight = 1,
            Ascent = -1, //var height = (GlyphTypeface.Descent - GlyphTypeface.Ascent + GlyphTypeface.LineGap) * Scale; //todo: need to consult Avalonia guys
            Descent = 0,
            LineGap = 0,
            UnderlinePosition = -1,
            UnderlineThickness = DrawingContextImpl.UnderlineThickness,
            StrikethroughPosition = -1,
            StrikethroughThickness = DrawingContextImpl.StrikethroughThickness,
            IsFixedPitch = true
        };

        public void Dispose()
        {
            lock (GlyphCacheSync)
            {
                GlyphTextByIndex.Clear();
                GlyphWidthByIndex.Clear();
                GlyphIndexByText.Clear();
            }
        }

        public bool TryGetGlyphMetrics(ushort glyph, out GlyphMetrics metrics)
        {
            lock (GlyphCacheSync)
            {
                if (GlyphWidthByIndex.TryGetValue(glyph, out ushort width))
                {
                    metrics = new GlyphMetrics
                    {
                        XBearing = 0,
                        YBearing = 0,
                        Height = 1,
                        Width = width
                    };
                    return true;
                }

                metrics = default;
                return false;
            }
        }

        public ushort GetGlyph(uint codepoint)
        {
            return GetGlyphIndex(char.ConvertFromUtf32((int)codepoint));
        }

        public bool TryGetGlyph(uint codepoint, out ushort glyph)
        {
            glyph = GetGlyphIndex(char.ConvertFromUtf32((int)codepoint));
            return true;
        }

        public ushort[] GetGlyphs(ReadOnlySpan<uint> codepoints)
        {
            ushort[] glyphs = new ushort[codepoints.Length];
            for (int i = 0; i < codepoints.Length; i++)
                glyphs[i] = GetGlyphIndex(char.ConvertFromUtf32((int)codepoints[i]));
            return glyphs;
        }

        public int GetGlyphAdvance(ushort glyph)
        {
            lock (GlyphCacheSync)
            {
                return GlyphWidthByIndex[glyph];
            }
        }

        public int[] GetGlyphAdvances(ReadOnlySpan<ushort> glyphs)
        {
            int[] advances = new int[glyphs.Length];
            lock (GlyphCacheSync)
            {
                for (int i = 0; i < glyphs.Length; i++) advances[i] = GetGlyphAdvance(glyphs[i]);
            }

            return advances;
        }

        public bool TryGetTable(uint tag, out byte[] table)
        {
            table = Array.Empty<byte>();
            return false;
        }

        public string FamilyName { get; } = FontManagerImpl.ConsoleDefaultFontFamily();
        public FontWeight Weight { get; init; } = FontWeight.Normal;
        public FontStyle Style { get; init; } = FontStyle.Normal;
        public FontStretch Stretch => FontStretch.Normal;
        public int GlyphCount => char.MaxValue;

        public FontMetrics Metrics { get; } = MetricsSingleton;

        public FontSimulations FontSimulations => FontSimulations.None;

#pragma warning disable CA1822 // Mark members as static
        public ushort GetGlyphIndex(string glyphText)
        {
            ushort glyph;
            lock (GlyphCacheSync)
            {
                if (!GlyphIndexByText.TryGetValue(glyphText, out glyph))
                {
                    if (GlyphIndexByText.Count >= ushort.MaxValue)
                        throw new InvalidOperationException("Glyph cache overflow.");
                    glyph = (ushort)GlyphTextByIndex.Count;
                    GlyphTextByIndex[glyph] = glyphText;
                    GlyphWidthByIndex[glyph] = glyphText.MeasureText();
                    GlyphIndexByText[glyphText] = glyph;
                }
            }

            return glyph;
        }

        public string GetGlyphText(ushort glyph)
        {
            lock (GlyphCacheSync)
            {
                if (!GlyphTextByIndex.TryGetValue(glyph, out string text))
                    throw new ArgumentException($"Glyph index {glyph} not found in cache.", nameof(glyph));
                return text;
            }
        }

        public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
        {
            if (!(options.Typeface is ConsoleTypeface))
                throw new ArgumentException("TextShaperOptions.Typeface must be of type ConsoleTypeface.",
                    nameof(options));

            var console = AvaloniaLocator.Current.GetRequiredService<IConsoleCapabilities>();

            IReadOnlyList<Grapheme> graphemes = Grapheme.Parse(text.Span.ToString(),
                console.Capabilities.HasFlag(ConsoleCapabilities.SupportsComplexEmoji));

            var shapedBuffer = new ShapedBuffer(text, graphemes.Count,
                this, 1, 0 /*todo: must be 1 for right to left?*/);
            for (ushort i = 0; i < shapedBuffer.Length; i++)
            {
                Grapheme grapheme = graphemes[i];
                ushort glyphIndex = GetGlyphIndex(grapheme.Glyph);
                int glyphAdvance = GetGlyphAdvance(glyphIndex);
                shapedBuffer[i] = new GlyphInfo(glyphIndex, grapheme.Cluster, glyphAdvance);
            }

            return shapedBuffer;
        }

        void IGlyphRunRender.DrawGlyphRun(DrawingContextImpl context, PixelPoint position, GlyphRunImpl glyphRun,
            Color foreground, out PixelRect rectToRefresh)
        {
            PixelPoint startPosition = position;

            // Handle empty glyph run (e.g., empty TextBox)
            // Avalonia will send us glyph run it has ginned up with with empty width to represent empty text.
            // we need to invalidate the starting character to make sure it's redrawn as empty.
            if (glyphRun.GlyphInfos.Count == 0 || glyphRun.Bounds.Width <= 0)
            {
                rectToRefresh = new PixelRect(startPosition, new PixelSize(1, 1));
                return;
            }

            foreach (GlyphInfo glyphInfo in glyphRun.GlyphInfos)
            {
                string glyph = GetGlyphText(glyphInfo.GlyphIndex);
                if (glyph == "\t")
                {
                    var symbol = new Symbol(' ', 1);
                    var newPixel = new Pixel(symbol, foreground, Style, Weight);

                    for (int i = 0; i < glyphInfo.GlyphAdvance; i++)
                    {
                        context.DrawPixel(newPixel, position);
                        position = position.WithX(position.X + 1);
                    }
                }
                else if (glyph == "\n" || glyph == "\r\n")
                {
                    context.DrawPixel(new Pixel(Symbol.Space, foreground, Style, Weight), position);
                    position = position.WithX(position.X + (int)glyphInfo.GlyphAdvance);
                }
                else
                {
                    var symbol = new Symbol(glyph, (byte)glyphInfo.GlyphAdvance);
                    context.DrawPixel(new Pixel(symbol, foreground, Style, Weight), position);
                    position = position.WithX(position.X + (int)glyphInfo.GlyphAdvance);
                }
            }

            rectToRefresh = new PixelRect(startPosition,
                new PixelSize(position.X - startPosition.X,
                    position.Y - startPosition.Y + 1));
        }

#pragma warning restore CA1822 // Mark members as static
    }
}