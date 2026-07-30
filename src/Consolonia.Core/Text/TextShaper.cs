using System;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Core.Text.Fonts;

namespace Consolonia.Core.Text
{
    public class TextShaper : ITextShaperImpl
    {
        public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
        {
            return ((ConsolePlatformTypeface)options.GlyphTypeface.PlatformTypeface).ConsoleTypeface.ShapeText(text,
                options);
        }

        public ITextShaperTypeface CreateTypeface(GlyphTypeface glyphTypeface)
        {
            return ((ConsolePlatformTypeface)glyphTypeface.PlatformTypeface).ConsoleTypeface;
        }
    }
}