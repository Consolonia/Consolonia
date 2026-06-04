using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Core.Drawing;

namespace Consolonia.Core.Text.Fonts
{
    /// <summary>
    ///     Typeface which is made up of multiple design em height typefaces
    /// </summary>
    public class AsciiArtFamilyTypeface : IConsoleTypeface
    {
        private readonly Dictionary<int, IConsoleTypeface> _typefaces = new();
        private bool _disposedValue;

        public AsciiArtFamilyTypeface(string name)
        {
            FamilyName = name;
        }

        internal IConsoleTypeface PrimaryTypeface => _typefaces[_typefaces.Keys.Max()];

        void IGlyphRunRender.DrawGlyphRun(DrawingContextImpl context, PixelPoint position, GlyphRunImpl glyphRun,
            Color foreground, out PixelRect rectToRefresh)
        {
            IConsoleTypeface typeface = GetTypeface((int)glyphRun.FontRenderingEmSize);
            var typefaceDrawing = typeface as IGlyphRunRender;
            ArgumentNullException.ThrowIfNull(typefaceDrawing);
            typefaceDrawing.DrawGlyphRun(context, position, glyphRun, foreground, out rectToRefresh);
        }

        public string FamilyName { get; init; }

        public FontWeight Weight => PrimaryTypeface.Weight;

        public FontStyle Style => PrimaryTypeface.Style;

        public FontStretch Stretch => PrimaryTypeface.Stretch;

        public int GlyphCount => PrimaryTypeface.GlyphCount;

        public FontMetrics Metrics => PrimaryTypeface.Metrics;

        public FontSimulations FontSimulations => FontSimulations.None;

        public ushort GetGlyph(uint codepoint)
        {
            return PrimaryTypeface.GetGlyph(codepoint);
        }

        public int GetGlyphAdvance(ushort glyph)
        {
            return PrimaryTypeface.GetGlyphAdvance(glyph);
        }

        public int[] GetGlyphAdvances(ReadOnlySpan<ushort> glyphs)
        {
            return PrimaryTypeface.GetGlyphAdvances(glyphs);
        }

        public ushort[] GetGlyphs(ReadOnlySpan<uint> codepoints)
        {
            return PrimaryTypeface.GetGlyphs(codepoints);
        }

        public bool TryGetGlyph(uint codepoint, out ushort glyph)
        {
            return PrimaryTypeface.TryGetGlyph(codepoint, out glyph);
        }

        public bool TryGetGlyphMetrics(ushort glyph, out GlyphMetrics metrics)
        {
            return PrimaryTypeface.TryGetGlyphMetrics(glyph, out metrics);
        }

        public bool TryGetTable(uint tag, out byte[] table)
        {
            return PrimaryTypeface.TryGetTable(tag, out table);
        }


        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
        {
            IConsoleTypeface typeface = GetTypeface((int)options.FontRenderingEmSize);
            return typeface.ShapeText(text, options);
        }

        internal void AddTypeface(IConsoleTypeface typeface)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            _typefaces[typeface.Metrics.DesignEmHeight] = typeface;
        }

        internal IConsoleTypeface GetTypeface(int designEmHeight)
        {
            if (_typefaces.Count == 0) throw new InvalidOperationException($"No typefaces available in {FamilyName}.");

            if (_typefaces.ContainsKey(designEmHeight)) return _typefaces[designEmHeight];
            int closest = _typefaces.Keys.OrderBy(k => Math.Abs(k - designEmHeight)).First();
            return _typefaces[closest];
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    foreach (IConsoleTypeface typeface in _typefaces.Values) typeface.Dispose();
                    _typefaces.Clear();
                }

                _disposedValue = true;
            }
        }
    }
}