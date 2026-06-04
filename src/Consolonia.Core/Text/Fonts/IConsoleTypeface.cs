using System;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Consolonia.Core.Text.Fonts
{
    internal interface IConsoleTypeface : ITextShaperTypeface, IGlyphRunRender, IDisposable
    {
        string FamilyName { get; }

        FontWeight Weight { get; }

        FontStyle Style { get; }

        FontStretch Stretch { get; }

        int GlyphCount { get; }

        FontMetrics Metrics { get; }

        FontSimulations FontSimulations { get; }

        ushort GetGlyph(uint codepoint);

        int GetGlyphAdvance(ushort glyph);

        int[] GetGlyphAdvances(ReadOnlySpan<ushort> glyphs);

        ushort[] GetGlyphs(ReadOnlySpan<uint> codepoints);

        bool TryGetGlyph(uint codepoint, out ushort glyph);

        bool TryGetGlyphMetrics(ushort glyph, out GlyphMetrics metrics);

        bool TryGetTable(uint tag, out byte[] table);

        ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options);
    }
}