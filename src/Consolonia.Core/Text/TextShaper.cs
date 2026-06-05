using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Core.Text.Fonts;

namespace Consolonia.Core.Text
{
    public class TextShaper : ITextShaperImpl
    {
        private readonly ITextShaperImpl _fallback;

        public TextShaper(ITextShaperImpl fallback = null)
        {
            _fallback = fallback;
        }

        public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
        {
            if (options.GlyphTypeface.PlatformTypeface is ConsolePlatformTypeface consolePlatformTypeface)
                return consolePlatformTypeface.ConsoleTypeface.ShapeText(text, options);

            if (_fallback != null)
                return _fallback.ShapeText(text, options);

            throw new KeyNotFoundException(
                "Unsupported glyph typeface and no fallback text shaper is configured.");
        }

        public ITextShaperTypeface CreateTypeface(GlyphTypeface glyphTypeface)
        {
            if (glyphTypeface.PlatformTypeface is ConsolePlatformTypeface consolePlatformTypeface)
                return consolePlatformTypeface.ConsoleTypeface;

            if (_fallback != null)
                return _fallback.CreateTypeface(glyphTypeface);

            throw new KeyNotFoundException(
                "Unsupported glyph typeface and no fallback text shaper is configured.");
        }
    }
}