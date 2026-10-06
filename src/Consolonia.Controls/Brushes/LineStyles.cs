using System;
using System.Collections.Generic;
using System.Linq;

namespace Consolonia.Controls.Brushes
{
    /// <summary>
    ///     Represents a LineStyle for each side of a rectangle.
    /// </summary>
    public class LineStyles
    {
        public LineStyles()
        {
            Left = Top = Right = Bottom = LineStyle.SingleLine;
        }

        public LineStyles(string text)
        {
            List<LineStyle> styles = text.Split(' ', ',')
                .Select(token => Enum.Parse<LineStyle>(token))
                .ToList();

            Left = styles.Count > 0 ? styles[0] : LineStyle.SingleLine;
            Top = styles.Count > 1 ? styles[1] : Left;
            Right = styles.Count > 2 ? styles[2] : Top;
            Bottom = styles.Count > 3 ? styles[3] : Right;
        }

        public LineStyles(LineStyle? left = null, LineStyle? top = null, LineStyle? right = null,
            LineStyle? bottom = null)
        {
            Left = left ?? LineStyle.SingleLine;
            Top = top ?? Left;
            Right = right ?? Top;
            Bottom = bottom ?? Right;
        }

        public LineStyle Left { get; set; }
        public LineStyle Top { get; set; }
        public LineStyle Right { get; set; }
        public LineStyle Bottom { get; set; }

        public LineStyles Clone()
        {
            return new LineStyles(Left, Top, Right, Bottom);
        }

        public static LineStyles Parse(string s)
        {
            return new LineStyles(s);
        }

#pragma warning disable CA2225 // Operator overloads have named alternates
        public static implicit operator LineStyles(string text)
        {
            return new LineStyles(text);
        }

        public static implicit operator LineStyles(LineStyle lineStyle)
        {
            return new LineStyles(lineStyle);
        }
#pragma warning restore CA2225 // Operator overloads have named alternates
    }
}