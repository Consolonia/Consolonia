using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using Avalonia.Platform;
using Consolonia.Core.Text.Fonts;

namespace Consolonia.Core.Text
{
    /// <summary>
    ///     https://docs.microsoft.com/en-us/typography/opentype/spec/ttch01#funits-and-the-em-square
    /// </summary>
    internal class FontManagerImpl : IFontManagerImpl
    {
        private readonly IFontManagerImpl _fallback;

        public FontManagerImpl(IFontManagerImpl fallback = null)
        {
            _fallback = fallback;
        }

        public string GetDefaultFontFamilyName()
        {
            return ConsoleDefaultFontFamily();
        }

        string[] IFontManagerImpl.GetInstalledFontFamilyNames(bool checkForUpdates)
        {
            return new[] { ConsoleDefaultFontFamily() };
        }

        public bool TryMatchCharacter(int codepoint, FontStyle fontStyle, FontWeight fontWeight,
            FontStretch fontStretch,
            string familyName, CultureInfo culture, out IPlatformTypeface typeface)
        {
            typeface = CreateConsolePlatformTypeface(new ConsoleTypeface
            {
                Weight = fontWeight,
                Style = fontStyle
            });
            return true;
        }

        public bool TryCreateGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
            out IPlatformTypeface glyphTypeface)
        {
            if (familyName == ConsoleDefaultFontFamily())
            {
                //todo: check font is ours the only
                glyphTypeface = CreateConsolePlatformTypeface(new ConsoleTypeface
                {
                    Weight = weight,
                    Style = style
                });
                return true;
            }

            glyphTypeface = null;
            return false;
        }

#pragma warning disable CA1822 // Mark members as static
        public bool TryCreateGlyphTypeface(Stream stream, FontSimulations fontSimulations,
            [NotNullWhen(true)] out IPlatformTypeface glyphTypeface)
#pragma warning restore CA1822 // Mark members as static
        {
            if (_fallback != null &&
                _fallback.TryCreateGlyphTypeface(stream, fontSimulations, out IPlatformTypeface fallbackTypeface))
            {
                glyphTypeface = new ConsolePlatformTypeface(new ConsoleTypeface(), fallbackTypeface);
                return true;
            }

            glyphTypeface = CreateConsolePlatformTypeface(new ConsoleTypeface());
            return true;
        }

#pragma warning disable CA1822 // Mark members as static
        public bool TryGetFamilyTypefaces(string familyName, out IReadOnlyList<Typeface> typefaces)
#pragma warning restore CA1822 // Mark members as static
        {
            if (familyName == ConsoleDefaultFontFamily())
            {
                typefaces = new[]
                {
                    new Typeface(ConsoleDefaultFontFamily(), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal)
                };
                return true;
            }

            typefaces = Array.Empty<Typeface>();
            return false;
        }

        public GlyphTypeface CreateGlyphTypeface(IConsoleTypeface consoleTypeface)
        {
            return new GlyphTypeface(CreateConsolePlatformTypeface(consoleTypeface));
        }

        private IPlatformTypeface CreateConsolePlatformTypeface(IConsoleTypeface consoleTypeface)
        {
            return new ConsolePlatformTypeface(consoleTypeface, CreateFallbackTypeface(consoleTypeface));
        }

        private IPlatformTypeface CreateFallbackTypeface(IConsoleTypeface consoleTypeface)
        {
            if (_fallback == null)
                return ConsolePlatformTypeface.CreateSystemFontFallbackTypeface(consoleTypeface);

            string fallbackFamily = _fallback.GetDefaultFontFamilyName();
            if (!string.IsNullOrEmpty(fallbackFamily) &&
                _fallback.TryCreateGlyphTypeface(fallbackFamily, consoleTypeface.Style, consoleTypeface.Weight,
                    consoleTypeface.Stretch, out IPlatformTypeface fallbackTypeface))
                return fallbackTypeface;

            if (_fallback.TryMatchCharacter(' ', consoleTypeface.Style, consoleTypeface.Weight, consoleTypeface.Stretch,
                    fallbackFamily, CultureInfo.CurrentCulture, out fallbackTypeface))
                return fallbackTypeface;

            return ConsolePlatformTypeface.CreateSystemFontFallbackTypeface(consoleTypeface);
        }

        public bool TryCreateGlyphTypeface(Stream stream, out IPlatformTypeface glyphTypeface)
        {
            return TryCreateGlyphTypeface(stream, FontSimulations.None, out glyphTypeface);
        }

        public static string ConsoleDefaultFontFamily()
        {
            return "ConsoleDefault";
        }
    }
}