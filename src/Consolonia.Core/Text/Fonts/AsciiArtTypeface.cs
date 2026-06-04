using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
#pragma warning disable CA1307 // Specify StringComparison for clarity
    /// <summary>
    ///     Fonts which use the TLF/Caca format
    /// </summary>
    public class AsciiArtTypeface : IConsoleTypeface
    {
        /// glyphindex to codepoint
        private readonly Dictionary<uint, ushort> _codepointsToIndex = new();

        /// codepoint to glyph
        private readonly Dictionary<uint, AsciiArtGlyph> _glyphs = new();

        private readonly Dictionary<ushort, uint> _indexToCodepoint = new();
        private bool _disposedValue;

        public AsciiArtTypeface(string familyName)
        {
            FamilyName = familyName;
            Weight = FontWeight.Normal;
            Style = FontStyle.Normal;
            Stretch = FontStretch.Normal;
            FontSimulations = FontSimulations.None;
        }

        public char Hardblank { get; set; } = '$';

        /// <summary>
        ///     Layout mode flags that control character smushing and kerning behavior
        /// </summary>
        public LayoutMode LayoutMode { get; set; } = LayoutMode.None;

        /// <summary>
        ///     Legacy layout mode flags (pre-FIGlet 2a). This is deprecated and replaced by LayoutMode.
        ///     Preserved for compatibility and reference purposes only.
        /// </summary>
        public OldLayoutMode OldLayoutMode { get; set; } = OldLayoutMode.None;

        public int Width { get; set; }

        public int Height { get; set; }

        void IGlyphRunRender.DrawGlyphRun(DrawingContextImpl context, PixelPoint position, GlyphRunImpl glyphRun,
            Color foreground, out PixelRect rectToRefresh)
        {
            // Handle empty glyph run (e.g., empty TextBox)
            // Avalonia will send us glyph run it has ginned up with with empty width to represent empty text.
            // we need to invalidate the starting character to make sure it's redrawn as empty.
            if (glyphRun.GlyphInfos.Count == 0 || glyphRun.Bounds.Width <= 0)
            {
                rectToRefresh = new PixelRect(position, new PixelSize(Width, Height));
                return;
            }

            bool smushing = LayoutMode.HasFlag(LayoutMode.Smush) || LayoutMode.HasFlag(LayoutMode.Kern);
            PixelPoint startPosition = position;
            var pos = new PixelPoint(position.X, position.Y);
            foreach (GlyphInfo glyphInfo in glyphRun.GlyphInfos.Where(gi => gi.GlyphIndex != ushort.MaxValue))
            {
                uint codepoint = _indexToCodepoint[glyphInfo.GlyphIndex];
                AsciiArtGlyph asciiGlyph = _glyphs[codepoint];
                foreach (Grapheme[] graphemeLine in asciiGlyph.GraphemeLines)
                {
                    int iChar = pos.X;
                    foreach (Grapheme grapheme in graphemeLine)
                    {
                        var symbol = new Symbol(grapheme.Glyph);
                        // When we draw with smushing we need to skip whitespace but not for glyphs that are whitespace themselves.
                        if (!smushing ||
                            codepoint == 0x2009 ||
                            codepoint == '\t' ||
                            grapheme.Glyph != " ")
                            context.DrawPixel(new Pixel(new PixelForeground(symbol, foreground, Weight, Style)),
                                pos.WithX(iChar));
                        iChar += symbol.Width;
                    }

                    pos = pos.WithY(pos.Y + 1);
                }

                pos = pos.WithX(pos.X + (ushort)glyphInfo.GlyphAdvance).WithY(position.Y);
            }

            rectToRefresh =
                new PixelRect(startPosition, new PixelSize(pos.X - startPosition.X, Metrics.DesignEmHeight));
        }

        public string FamilyName { get; init; }

        public FontWeight Weight { get; init; }

        public FontStyle Style { get; init; }

        public FontStretch Stretch { get; init; }

        public int GlyphCount => _glyphs.Count;

        public FontMetrics Metrics { get; set; }

        public FontSimulations FontSimulations { get; init; }

        public ushort GetGlyph(uint codepoint)
        {
            if (TryGetGlyph(codepoint, out ushort glyph))
                return glyph;
            return ushort.MaxValue;
        }

        public int GetGlyphAdvance(ushort glyph)
        {
            if (glyph == ushort.MaxValue)
                return 0;
            uint codepoint = _indexToCodepoint[glyph];
            AsciiArtGlyph asciiGlyph = _glyphs[codepoint];
            return asciiGlyph.Width;
        }

        public int[] GetGlyphAdvances(ReadOnlySpan<ushort> glyphs)
        {
            int[] advances = new int[glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++) advances[i] = GetGlyphAdvance(glyphs[i]);
            return advances;
        }

        public ushort[] GetGlyphs(ReadOnlySpan<uint> codepoints)
        {
            ushort[] glyphs = new ushort[codepoints.Length];
            for (int i = 0; i < codepoints.Length; i++) glyphs[i] = GetGlyph(codepoints[i]);
            return glyphs;
        }

        public bool TryGetGlyph(uint codepoint, out ushort glyph)
        {
            if (!_codepointsToIndex.TryGetValue(codepoint, out glyph))
                // THIN SPACE 0x2009 used as codepoint for unknown glyphs
                if (!_codepointsToIndex.TryGetValue(0x2009, out glyph))
                    glyph = AddGlyph(0x2009,
                        new AsciiArtGlyph(this, 0x2009, Enumerable.Repeat(" ", Metrics.DesignEmHeight).ToArray()));

            return true;
        }

        public bool TryGetGlyphMetrics(ushort glyph, out GlyphMetrics metrics)
        {
            metrics = new GlyphMetrics
            {
                XBearing = 0,
                YBearing = 0,
                Height = Metrics.DesignEmHeight,
                Width = (ushort)GetGlyphAdvance(glyph)
            };
            return true;
        }

        public bool TryGetTable(uint tag, out byte[] table)
        {
            table = Array.Empty<byte>();
            return false;
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
        {
            IReadOnlyList<Grapheme> graphemes = Grapheme.Parse(text.Span.ToString(), false);

            var glyphs = new AsciiArtGlyph[graphemes.Count];
            ushort[] glyphIndices = new ushort[graphemes.Count];
            for (int i = 0; i < graphemes.Count; i++)
            {
                Grapheme grapheme = graphemes[i];
                uint codepoint = grapheme.Glyph[0];
                glyphIndices[i] = GetGlyph(codepoint);
                codepoint = _indexToCodepoint[glyphIndices[i]];
                glyphs[i] = _glyphs[codepoint];
            }

            byte[] advances;
            if (LayoutMode.HasFlag(LayoutMode.Kern) || LayoutMode.HasFlag(LayoutMode.Smush))
                advances = CalculateAdvancesWithKerning(glyphs);
            else
                advances = glyphs.Select(g => g.Width).ToArray();


            var shapedBuffer =
                new ShapedBuffer(text, graphemes.Count, options.GlyphTypeface, Metrics.DesignEmHeight,
                    options.BidiLevel);

            for (int i = 0; i < shapedBuffer.Length; i++)
                shapedBuffer[i] = new GlyphInfo(glyphIndices[i], i, advances[i], default);
            return shapedBuffer;
        }

        public ushort AddGlyph(uint codepoint, AsciiArtGlyph glyph)
        {
            if (codepoint == ' ')
            {
                // generate a TAB glyph based on space advance, none of these fonts have tab glyphs
                uint tabCodepoint = '\t';
                ushort advance = "\t".MeasureText();
                AddGlyph(tabCodepoint, new AsciiArtGlyph(glyph.Typeface, tabCodepoint, glyph.Lines.Select(line =>
                {
                    // we duplicate the "space" definition for each line "Advance" (4) times to make a tab
                    var sb = new StringBuilder();
                    for (int i = 0; i < advance; i++)
                        sb.Append(line);
                    return sb.ToString();
                }).ToArray()));
            }

            _glyphs[codepoint] = glyph;
            ushort index = (ushort)_codepointsToIndex.Count;
            _codepointsToIndex[codepoint] = index;
            _indexToCodepoint[index] = codepoint;
            return index;
        }

        /// <summary>
        ///     Calculate advances for a sequence of glyphs with kerning and smush support
        /// </summary>
        /// <param name="glyphs">Sequence of glyphs to process</param>
        /// <returns>Array of advances for each glyph</returns>
        public byte[] CalculateAdvancesWithKerning(ReadOnlySpan<AsciiArtGlyph> glyphs)
        {
            if (glyphs.Length == 0)
                return Array.Empty<byte>();

            byte[] advances = new byte[glyphs.Length];

            for (int i = 1; i < glyphs.Length; i++)
            {
                AsciiArtGlyph leftGlyph = glyphs[i - 1];
                AsciiArtGlyph rightGlyph = glyphs[i];

                if (rightGlyph.Codepoint == ' ' ||
                    rightGlyph.Codepoint == 0x2009 || // THIN SPACE
                    rightGlyph.Codepoint == '\t')
                {
                    // space glyph, no kerning/smushing
                    advances[i - 1] = leftGlyph.Width;
                    continue;
                }

                int maxOverlap = CalculateMaxOverlap(leftGlyph, rightGlyph);

                // Advance is the width minus the overlap; clamp to avoid underflow
                int width = leftGlyph.Width;
                int overlap = Math.Min(maxOverlap, width);
                int advance = Math.Max(0, width - overlap);
                advances[i - 1] = (byte)advance;
            }

            advances[^1] = glyphs[^1].Width;
            return advances;
        }

        /// <summary>
        ///     Calculate the maximum overlap (kerning/smush amount) between two glyphs
        /// </summary>
        private int CalculateMaxOverlap(AsciiArtGlyph leftGlyph, AsciiArtGlyph rightGlyph)
        {
            int minMove = int.MaxValue;

            // Check each line to find the minimum move (tightest constraint)
            int lineCount = Math.Max(leftGlyph.Lines.Length, rightGlyph.Lines.Length);

            for (int lineIdx = 0; lineIdx < lineCount; lineIdx++)
            {
                string leftLine = lineIdx < leftGlyph.Lines.Length ? leftGlyph.Lines[lineIdx] : "";
                string rightLine = lineIdx < rightGlyph.Lines.Length ? rightGlyph.Lines[lineIdx] : "";

                int leftEnd = lineIdx < leftGlyph.Ends.Length ? leftGlyph.Ends[lineIdx] : -1;
                int rightStart = lineIdx < rightGlyph.Starts.Length ? rightGlyph.Starts[lineIdx] : rightLine.Length;

                int spaceAfter = leftEnd < 0 ? leftLine.Length : leftLine.Length - leftEnd - 1;
                int spaceBefore = rightStart >= rightLine.Length ? rightLine.Length : rightStart;

                // Base move is just the sum of whitespace margins
                int move = spaceAfter + spaceBefore;

                // Check if we can smush at the boundary
                if (leftEnd >= 0 && rightStart < rightLine.Length)
                {
                    char leftBackChar = leftLine[leftEnd];
                    char rightFrontChar = rightLine[rightStart];

                    if (LayoutMode.HasFlag(LayoutMode.Smush) &&
                        TrySmush(leftBackChar, rightFrontChar) != '\0')
                        // If we can smush, we can move one character closer
                        move++;
                }

                // Can't move more than the width of the right character's content
                move = Math.Min(move, rightLine.Length);

                // Track the minimum move across all lines (tightest constraint)
                if (move < minMove)
                    minMove = move;
            }

            // If we never found any valid line, return 0
            if (minMove == int.MaxValue)
                return 0;

            return Math.Max(0, minMove);
        }

        /// <summary>
        ///     Check if two characters can smush together, returning the resulting character or '\0' if they can't
        /// </summary>
        private char TrySmush(char leftChar, char rightChar)
        {
            // If either is space, they can't smush (but can kern)
            if (leftChar == ' ' || rightChar == ' ')
                return '\0';

            // Hardblank is treated specially
            if (leftChar == Hardblank || rightChar == Hardblank)
            {
                if (LayoutMode.HasFlag(LayoutMode.Hardblank))
                    return leftChar == Hardblank ? rightChar : leftChar;
                return '\0';
            }

            if (LayoutMode.HasFlag(LayoutMode.Equal))
                if (leftChar == rightChar)
                    return leftChar;

            if (LayoutMode.HasFlag(LayoutMode.Lowline))
            {
                if (leftChar == '_' && "|/\\[]{}()<>".Contains(rightChar))
                    return rightChar;
                if (rightChar == '_' && "|/\\[]{}()<>".Contains(leftChar))
                    return leftChar;
            }

            if (LayoutMode.HasFlag(LayoutMode.Hierarchy))
            {
                string hierarchy = "|/\\[]{}()<>";
                int leftLevel = hierarchy.IndexOf(leftChar);
                int rightLevel = hierarchy.IndexOf(rightChar);

                if (leftLevel >= 0 && rightLevel >= 0)
                    return leftLevel > rightLevel ? leftChar : rightChar;
            }

            if (LayoutMode.HasFlag(LayoutMode.Pair))
            {
                if (leftChar == '[' && rightChar == ']' || leftChar == ']' && rightChar == '[')
                    return '|';
                if (leftChar == '{' && rightChar == '}' || leftChar == '}' && rightChar == '{')
                    return '|';
                if (leftChar == '(' && rightChar == ')' || leftChar == ')' && rightChar == '(')
                    return '|';
            }

            if (LayoutMode.HasFlag(LayoutMode.BigX))
            {
                if (leftChar == '/' && rightChar == '\\')
                    return '|';
                if (leftChar == '\\' && rightChar == '/')
                    return 'Y';
                if (leftChar == '>' && rightChar == '<')
                    return 'X';
            }

            return '\0';
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                }

                _disposedValue = true;
            }
        }
    }
}
