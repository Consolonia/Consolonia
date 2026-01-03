using System;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Reactive;
using NeoSmart.Unicode;
using Wcwidth;

namespace Consolonia.Controls
{
    public static class ControlUtils
    {
        private static readonly Lazy<IConsoleCapabilities> ConsoleCapabilities =
            new(() => AvaloniaLocator.Current.GetService<IConsoleCapabilities>());

        public static IDisposable SubscribeAction<TValue>(
            this IObservable<AvaloniaPropertyChangedEventArgs<TValue>> observable,
            Action<AvaloniaPropertyChangedEventArgs<TValue>> action)
        {
            return observable.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<TValue>>(action));
        }

        public static string GetStyledPropertyName([CallerMemberName] string propertyFullName = null)
        {
            return propertyFullName![..^8];
        }

        /// <summary>
        ///     Measure text for actual width
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static ushort MeasureText(this string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            IConsoleCapabilities console = ConsoleCapabilities.Value;
            ArgumentNullException.ThrowIfNull(console, nameof(console));
            bool supportsComplexEmoji = console.Capabilities.HasFlag(Controls.ConsoleCapabilities.SupportsComplexEmoji);

            ushort width = 0;
            ushort lastWidth = 0;
            int regionalRuneCount = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                int runeWidth = Emoji.IsEmoji(rune.ToString()) ? 2 : UnicodeCalculator.GetWidth(rune);
                if (runeWidth >= 0)
                {
                    if (rune.Value == Emoji.ZeroWidthJoiner || rune.Value == Emoji.ObjectReplacementCharacter)
                    {
                        if (supportsComplexEmoji)
                            width -= lastWidth;
                        else
                            // we return the first emoji as the result because terminal doesn't support chaining them
                            break;
                    }
                    else if (rune.Value == Codepoints.VariationSelectors.EmojiSymbol &&
                             lastWidth == 1)
                    {
                        // adjust for the emoji presentation, which is width 2
                        width++;
                        lastWidth = 2;
                    }
                    else if (rune.Value == Codepoints.VariationSelectors.TextSymbol &&
                             lastWidth == 2)
                    {
                        // adjust for the text presentation, which is width 1
                        width--;
                        lastWidth = 1;
                    }
                    else if (lastWidth > 0 &&
                             (rune.Value >= Emoji.SkinTones.Light && rune.Value <= Emoji.SkinTones.Dark ||
                              rune.Value == Codepoints.Keycap))
                    {
                        // Emoji modifier (skin tone) or keycap extender should continue current glyph

                        // else: combining — ignore
                    }
                    // regional indicator symbols
                    else if (rune.Value >= 0x1F1E6 && rune.Value <= 0x1F1FF)
                    {
                        regionalRuneCount++;
                        if (regionalRuneCount % 2 == 0)
                            // every pair of regional indicator symbols form a single glyph
                            width += (ushort)runeWidth;
                        // If the last rune is a regional indicator symbol, continue the current glyph
                    }
                    else
                    {
                        width += (ushort)runeWidth;
                    }


                    if (runeWidth > 0) lastWidth = (ushort)runeWidth;
                }
                // Control chars return as width < 0
                else
                {
                    if (rune.Value == 0x9 /* tab */)
                    {
                        // Avalonia uses hard coded 4 spaces for tabs (NOT column based tabstops), this may change in the future
                        width += 4;
                        lastWidth = 4;
                    }
                    else if (rune.Value == '\n')
                    {
                        width += 1;
                        lastWidth = 1;
                    }
                }
            }

            return width;
        }

        public static Color GetContrastColor(this Color color)
        {
            // Calculate relative luminance using the formula from WCAG 2.0
            // https://www.w3.org/TR/WCAG20/#relativeluminancedef
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            // Choose black or white based on which provides better contrast
            // White luminance = 1.0, Black luminance = 0.0
            double contrastWithWhite = (1.0 + 0.05) / (luminance + 0.05);
            double contrastWithBlack = (luminance + 0.05) / (0.0 + 0.05);
            Color result = contrastWithWhite > contrastWithBlack ? Colors.White : Colors.Black;
            return result;
        }


    }
}