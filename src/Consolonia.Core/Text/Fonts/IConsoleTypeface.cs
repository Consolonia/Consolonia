using System;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Consolonia.Core.Text.Fonts
{
    //todo: research if we still need this in case of Avalonia 12, there is similar sturcture already
    internal interface IConsoleTypeface : ITextShaperTypeface, IGlyphRunRender
    {
        string FamilyName { get; }

        FontWeight Weight { get; }

        FontStyle Style { get; }

        FontStretch Stretch { get; }

        int GlyphCount { get; }

        FontMetrics
            Metrics
        {
            get;
        } // todo: in case of simple font we are already having metrics inside Typeface loaded by Avalonia, then why do we need it here and this entire abstraction. Why can't we use Avalonian GlyphTypeface?

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