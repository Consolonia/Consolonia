using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor
{
    public class EgaConsoleColorMode : IConsoleColorMode
    {
        public static readonly Lazy<EgaConsoleColorMode> Instance = new(() =>
        {
            try
            {
                return (EgaConsoleColorMode)AvaloniaLocator.Current.GetRequiredService<IConsoleColorMode>();
            }
            catch (InvalidCastException exception)
            {
                throw new ConsoloniaException(
                    "EgaConsoleColorMode is not supported on this platform. ",
                    exception);
            }
        });

        private readonly (ConsoleColor Color, (int R, int G, int B) Rgb)[] _backgroundConsoleColorMap;
        private readonly (ConsoleColor Color, (int R, int G, int B) Rgb)[] _consoleColorMap;
        private readonly bool _supportBrightBackground;

        public EgaConsoleColorMode(bool supportBrightBackground)
        {
            _supportBrightBackground = supportBrightBackground;
            _consoleColorMap =
            [
                (ConsoleColor.Black, (0, 0, 0)),
                (ConsoleColor.DarkBlue, (0, 0, 128)),
                (ConsoleColor.DarkGreen, (0, 128, 0)),
                (ConsoleColor.DarkCyan, (0, 128, 128)),
                (ConsoleColor.DarkRed, (128, 0, 0)),
                (ConsoleColor.DarkMagenta, (128, 0, 128)),
                (ConsoleColor.DarkYellow, (128, 128, 0)),
                (ConsoleColor.Gray, (192, 192, 192)),

                // 4th bit further
                (ConsoleColor.DarkGray, (128, 128, 128)),
                (ConsoleColor.Blue, (0, 0, 255)),
                (ConsoleColor.Green, (0, 255, 0)),
                (ConsoleColor.Cyan, (0, 255, 255)),
                (ConsoleColor.Red, (255, 0, 0)),
                (ConsoleColor.Magenta, (255, 0, 255)),
                (ConsoleColor.Yellow, (255, 255, 0)),
                (ConsoleColor.White, (255, 255, 255))
            ];

            _backgroundConsoleColorMap = supportBrightBackground ? _consoleColorMap : [.._consoleColorMap.Take(8)];
        }

        /// <inheritdoc />
        public Color Blend(Color color1, Color color2, bool isTargetForeground)
        {
            (ConsoleColor consoleColor1, EgaColorMode mode1) = ConvertToConsoleColorMode(color1, isTargetForeground);
            (ConsoleColor consoleColor2, EgaColorMode mode2) = ConvertToConsoleColorMode(color2, true);

            switch (mode2)
            {
                case EgaColorMode.Transparent:
                    return color1;

                case EgaColorMode.Shaded when mode1 == EgaColorMode.Shaded:
                {
                    ConsoleColor doubleShadedColor =
                        Shade(Shade(consoleColor1, isTargetForeground), isTargetForeground);
                    return ConvertToAvaloniaColor(doubleShadedColor, isTargetForeground);
                }
                case EgaColorMode.Shaded:
                {
                    ConsoleColor shadedColor = Shade(consoleColor1, isTargetForeground);
                    return ConvertToAvaloniaColor(shadedColor, isTargetForeground);
                }
                case EgaColorMode.Colored:
                    return ConvertToAvaloniaColor(consoleColor2, isTargetForeground);
                default:
                    throw new ArgumentOutOfRangeException(nameof(color2));
            }
        }

        /// <inheritdoc />
        public (object background, object foreground) MapColors(Color background, Color foreground, FontWeight? weight)
        {
            (ConsoleColor backgroundConsoleColor, EgaColorMode mode) = ConvertToConsoleColorMode(background, false);
            if (mode is not EgaColorMode.Colored)
                return ConsoloniaPlatform.RaiseNotSupported<(object background, object foreground)>(
                    NotSupportedRequestCode.BackgroundWasNotColoredWhileMapping, this, background, foreground, weight);

            (ConsoleColor foregroundConsoleColor, _) = ConvertToConsoleColorMode(foreground, true);
            //todo: if mode is transparent, don't print foreground. if shaded - shade it

            return (backgroundConsoleColor, foregroundConsoleColor);
        }

        public (ConsoleColor, EgaColorMode) ConvertToConsoleColorMode(Color color, bool isForeground)
        {
            ConsoleColor consoleColor = MapToConsoleColor(color, isForeground);

            EgaColorMode mode = color.A switch
            {
                <= 63 => EgaColorMode.Transparent,
                <= 191 => EgaColorMode.Shaded,
                _ => EgaColorMode.Colored
            };

            return (consoleColor, mode);
        }

        private Dictionary<Color, ConsoleColor> _consoleColorCache = new();

        private ConsoleColor MapToConsoleColor(Color color, bool isForeground)
        {
            if (_consoleColorCache.TryGetValue(color, out ConsoleColor cachedColor))
                return cachedColor;
            int r = color.R, g = color.G, b = color.B;

            // Find the nearest ConsoleColor by RGB distance
            var consoleColor= GetPalette(isForeground)
                .OrderBy(c => Math.Pow(c.Rgb.R - r, 2) + Math.Pow(c.Rgb.G - g, 2) + Math.Pow(c.Rgb.B - b, 2))
                .First().Color;
            _consoleColorCache[color] = consoleColor;
            return consoleColor;
        }

        private IEnumerable<(ConsoleColor Color, (int R, int G, int B) Rgb)> GetPalette(bool isForeground)
        {
            return isForeground ? _consoleColorMap : _backgroundConsoleColorMap;
        }

        private ConsoleColor Shade(ConsoleColor color, bool isForeground)
        {
            ConsoleColor fullScaleColor = color switch
            {
                ConsoleColor.White => ConsoleColor.Gray,
                ConsoleColor.Gray => ConsoleColor.DarkGray,
                ConsoleColor.Blue => ConsoleColor.DarkBlue,
                ConsoleColor.Green => ConsoleColor.DarkGreen,
                ConsoleColor.Cyan => ConsoleColor.DarkCyan,
                ConsoleColor.Red => ConsoleColor.DarkRed,
                ConsoleColor.Magenta => ConsoleColor.DarkMagenta,
                ConsoleColor.Yellow => ConsoleColor.DarkYellow,
                _ => ConsoleColor.Black
            };

            if (!_supportBrightBackground)
            {
                if (isForeground)
                {
                    // background becomes black very fast in this scenario, thus we are saturating foreground to avoid "everything is black"
                    if (fullScaleColor == ConsoleColor.Black)
                        fullScaleColor = ConsoleColor.DarkGray;
                }
                else
                {
                    if (fullScaleColor == ConsoleColor.DarkGray)
                        fullScaleColor = ConsoleColor.Black;
                }
            }

            return fullScaleColor;
        }

        public Color ConvertToAvaloniaColor(ConsoleColor consoleColor, bool isForeground,
            EgaColorMode mode = EgaColorMode.Colored)
        {
            switch (mode)
            {
                case EgaColorMode.Transparent:
                    return Color.FromArgb(0, 0, 0, 0);
                case EgaColorMode.Shaded:
                    return Color.FromArgb(127, 0, 0, 0);
                case EgaColorMode.Colored:
                    (ConsoleColor _, (int R, int G, int B) rgb) =
                        GetPalette(isForeground).First(c => c.Color == consoleColor);
                    return Color.FromRgb((byte)rgb.R, (byte)rgb.G, (byte)rgb.B);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
    }
}