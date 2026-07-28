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

        public bool TryCreateGlyphTypeface(Stream stream, FontSimulations fontSimulations,
            [NotNullWhen(true)] out IPlatformTypeface glyphTypeface)
        {
            glyphTypeface = new ConsolePlatformTypeface(new ConsoleTypeface());
            return true;
        }


        public bool TryGetFamilyTypefaces(string familyName, out IReadOnlyList<Typeface> typefaces)
        {
            if (familyName == ConsoleDefaultFontFamily())
            {
                typefaces =
                [
                    new Typeface(ConsoleDefaultFontFamily())
                ];
                return true;
            }

            typefaces = [];
            return false;
        }

        public static GlyphTypeface CreateGlyphTypeface(IConsoleTypeface consoleTypeface)
        {
            return new GlyphTypeface(CreateConsolePlatformTypeface(consoleTypeface));
        }

        private static IPlatformTypeface CreateConsolePlatformTypeface(IConsoleTypeface consoleTypeface)
        {
            return new ConsolePlatformTypeface(consoleTypeface);
        }

        public static string ConsoleDefaultFontFamily()
        {
            return "ConsoleDefault";
        }
    }
}