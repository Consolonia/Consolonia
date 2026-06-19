using System;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Core.Infrastructure;
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

            return ConsoloniaPlatform.RaiseNotSupported<ShapedBuffer>(
                NotSupportedRequestCode.TextShapingNotSupported, this, text, options);
        }

        public ITextShaperTypeface CreateTypeface(GlyphTypeface glyphTypeface)
        {
            if (glyphTypeface.PlatformTypeface is ConsolePlatformTypeface consolePlatformTypeface)
                return consolePlatformTypeface.ConsoleTypeface;

            if (_fallback != null)
                return _fallback.CreateTypeface(glyphTypeface);

            return ConsoloniaPlatform.RaiseNotSupported<ITextShaperTypeface>(
                NotSupportedRequestCode.TextShaperTypefaceNotSupported, this, glyphTypeface);
        }
    }
}
