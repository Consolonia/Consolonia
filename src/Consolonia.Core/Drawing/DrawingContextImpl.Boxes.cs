//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.InternalHelpers;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Lines and boxes methods
    /// </summary>
    internal partial class DrawingContextImpl
    {
        private const byte VerticalStartPattern = 0b0010;
        private const byte VerticalLinePattern = 0b1010;
        private const byte VerticalEndPattern = 0b1000;
        private const byte HorizontalStartPattern = 0b0100;
        private const byte HorizontalLinePattern = 0b0101;
        private const byte HorizontalEndPattern = 0b0001;

        // these are magic values for mapping drawing of a line to escape instructions for text decorations around text.
        public const int UnderlineThickness = 10;
        public const int StrikethroughThickness = 11;

        private const int TopLeft = 0;
        private const int TopRight = 1;
        private const int BottomRight = 2;
        private const int BottomLeft = 3;

        private static readonly char[][] EdgeChars =
        [
            // LineStyle=Edge
            ['▕', '▁', '▏', '▔'],
            // LineStyle=EdgeWide
            ['▐', '▄', '▌', '▀']
        ];


        // top left, top right, bottom right, bottom left, 
        private static readonly char[][] EdgeCornerChars =
        [
            // LineStyle=Edge we don't draw chars for edge corners
            [char.MinValue, char.MinValue, char.MinValue, char.MinValue],
            // LineStyle=EdgeWide
            ['▗', '▖', '▘', '▝']
        ];

        public void DrawLine(IPen pen, Point p1, Point p2)
        {
            DrawLineInternal(pen, Line.CreateMyLine(p1, p2));
        }

        public void DrawGeometry(IBrush brush, IPen pen, IGeometryImpl geometry)
        {
            switch (geometry)
            {
                case Rectangle myRectangle:
                    Rect rect = myRectangle.Rect;
                    DrawRectangle(brush, pen, new RoundedRect(rect));
                    break;
                case Line myLine:
                    DrawLineInternal(pen, myLine);
                    break;
                case StreamGeometryImpl streamGeometry:
                {
                    // if we have fills to do and a brush with opacity
                    if (brush != null &&
                        brush.Opacity > 0 &&
                        streamGeometry.Fills.Count > 0)
                        foreach (Rectangle fill in streamGeometry.Fills)
                            // Investigate: Does the pen apply to rectangle or not?
                            DrawRectangle(brush, null, new RoundedRect(fill.Rect));

                    // if we have strokes to draw, and a valid pen 
                    if (pen != null &&
                        pen.Thickness > 0 &&
                        pen.Brush != null &&
                        pen.Brush.Opacity > 0 &&
                        streamGeometry.Strokes.Count > 0)
                    {
                        RectangleLinePosition[] strokePositions = InferStrokePositions(streamGeometry);
                        for (int iStroke = 0; iStroke < streamGeometry.Strokes.Count; iStroke++)
                        {
                            Line stroke = streamGeometry.Strokes[iStroke];
                            RectangleLinePosition strokePosition = strokePositions[iStroke];
                            if (strokePosition == RectangleLinePosition.Left)
                                DrawLineInternal(pen, stroke, RectangleLinePosition.Left);
                            else if (strokePosition == RectangleLinePosition.Right)
                                DrawLineInternal(pen, stroke, RectangleLinePosition.Right);
                            else if (strokePosition == RectangleLinePosition.Top)
                                DrawLineInternal(pen, stroke, RectangleLinePosition.Top);
                            else if (strokePosition == RectangleLinePosition.Bottom)
                                DrawLineInternal(pen, stroke, RectangleLinePosition.Bottom);
                        }
                    }
                }
                    break;
                default:
                    ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.DrawGeometryNotSupported, this, brush,
                        pen, geometry);
                    break;
            }
        }

        /// <summary>
        ///     Draw a rectangle, rounded rectangles are not supported and will be drawn as normal rectangles.
        /// </summary>
        /// <remarks>
        ///     NOTE: Rect in avalonia has interesting semantics
        ///     Left and Top are INCLUSIVE
        ///     Right and Bottom are EXCLUSIVE.
        ///     So width=3 and height=3 means pixels at 0,1,2 even though Right = 4 and Bottom = 4
        ///     Bottom and Right cells should NOT be drawn into.
        /// </remarks>
        /// <param name="brush"></param>
        /// <param name="pen"></param>
        /// <param name="roundedRect"></param>
        /// <param name="boxShadows"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void DrawRectangle(IBrush brush, IPen pen, RoundedRect roundedRect, BoxShadows boxShadows = new())
        {
            if (roundedRect.Rect.IsEmpty()) return;

            if (roundedRect.IsRounded)
                ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.DrawingRoundedOrNonUniformRectandle, this,
                    brush, pen, roundedRect, boxShadows);

            Rect rect = roundedRect.Rect;
            if (pen != null && brush != null)
            {
                // This is one of those places where Avalonia/Consolonia don't align well due to character nature of consolonia.
                //
                // in this case the Rectangle geometry passes us a rect that is 1 pixel smaller than the pen thickness
                // based on the brush we need to adjust
                // * single/doubleline brushes we need to expand the fill to be 1 pixel larger on each side
                // * Edge brushes we need to shrink the fill to be 1 pixel smaller on each side
                if (pen.Brush is LineBrush lineBrush && lineBrush.HasEdgeLineStyle())
                    // shrink fill so that edge pen can be drawn around it.
                    DrawRectangleInternal(brush, null,
                        new Rect(rect.Position.X + 1, rect.Position.Y + 1, rect.Width - 1, rect.Height - 1));
                else
                    // increase fill so that it includes the border pen.
                    DrawRectangleInternal(brush, null,
                        new Rect(rect.Position, new Size(rect.Size.Width + 1, rect.Size.Height + 1)));
                DrawRectangleInternal(null, pen, rect, boxShadows);
            }
            else
            {
                // just draw the brush or pen 
                DrawRectangleInternal(brush, pen, rect, boxShadows);
            }
        }


        private void DrawRectangleInternal(IBrush brush, IPen pen, Rect rect, BoxShadows boxShadows = new())
        {
            if (brush == null && pen == null) return; //this is simple Panel for example

            Rect rectangleRect = rect.TransformToAABB(Transform);
            if (boxShadows.Count > 0)
                foreach (BoxShadow boxShadow in boxShadows)
                    // BoxShadow none is OK
                    // aka offSetX=0, offSetY=0, color=Transparent
                    if (boxShadow.OffsetX != 0 ||
                        boxShadow.OffsetY != 0 ||
                        boxShadow.Color != Colors.Transparent)
                        ConsoloniaPlatform.RaiseNotSupported(
                            NotSupportedRequestCode.DrawingBoxShadowNotSupported, this, brush, pen, rect, boxShadows);

            if (brush is not null)
            {
                switch (brush)
                {
                    case VisualBrush:
                        throw new NotImplementedException();
                    case ISceneBrush sceneBrush:
                    {
                        ISceneBrushContent sceneBrushContent = sceneBrush.CreateContent();
                        sceneBrushContent?.Render(this, Matrix.Identity);
                        return;
                    }
                    case MoveConsoleCaretToPositionBrush moveBrush:
                    {
                        var head = rectangleRect.TopLeft.ToPixelPoint();
                        if (CurrentClip.ContainsExclusive(head))
                        {
                            Pixel pixel = _pixelBuffer[head];
                            if (pixel.CaretStyle != moveBrush.CaretStyle)
                            {
                                // only be dirty if something changed
                                _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(head, new PixelSize(1, 1)));
                                _pixelBuffer[head] =
                                    pixel.Blend(new Pixel(moveBrush.CaretStyle));
                            }
                        }

                        return;
                    }
                }

                FillRectangleWithBrush(brush, rectangleRect.ToPixelRect());
            }

            if (pen is null
                or { Thickness: 0 }
                or { Brush: null }
                or { Brush: LineBrush { Brush: null } }) return;

            // NOTE: Line takes in untransformed Point, not PixelPoint and will be transformed inside DrawLineInternal
            DrawLineInternal(pen, new Line(rect.TopLeft, /*vertical: */ false, (int)rect.Width),
                RectangleLinePosition.Top);
            DrawLineInternal(pen, new Line(rect.BottomLeft, /*vertical: */ false, (int)rect.Width),
                RectangleLinePosition.Bottom);
            DrawLineInternal(pen, new Line(rect.TopLeft, /*vertical: */ true, (int)rect.Height),
                RectangleLinePosition.Left);
            DrawLineInternal(pen, new Line(rect.TopRight, /*vertical: */ true, (int)rect.Height),
                RectangleLinePosition.Right);
        }

        private static RectangleLinePosition[] InferStrokePositions(StreamGeometryImpl streamGeometry)
        {
            // infer rectangle hints by using focolpoint
            double focalPointX = streamGeometry.Strokes.Average(stroke =>
                stroke.PStart.X + Math.Abs(stroke.PStart.X - stroke.PEnd.X) / 2);
            double focalPointY = streamGeometry.Strokes.Average(stroke =>
                stroke.PStart.Y + Math.Abs(stroke.PStart.Y - stroke.PEnd.Y) / 2);
            var focalPoint = new Point(focalPointX, focalPointY);
            var strokePositions = new RectangleLinePosition[streamGeometry.Strokes.Count];

            for (int i = 0; i < streamGeometry.Strokes.Count; i++)
            {
                Line stroke = streamGeometry.Strokes[i];
                if (stroke.Bounds is { Width: 0, Height: 0 })
                {
                    // ignore zero length strokes
                    strokePositions[i] = RectangleLinePosition.Unknown;
                    continue;
                }

                if (stroke.Vertical)
                {
                    if (stroke.PStart.X <= focalPoint.X)
                        strokePositions[i] = RectangleLinePosition.Left;
                    else if (stroke.PStart.X >= focalPoint.X)
                        strokePositions[i] = RectangleLinePosition.Right;
                }
                else
                {
                    if (stroke.PStart.Y <= focalPoint.Y)
                        strokePositions[i] = RectangleLinePosition.Top;
                    else if (stroke.PStart.Y >= focalPoint.Y)
                        strokePositions[i] = RectangleLinePosition.Bottom;
                }
            }

            return strokePositions;
        }

        /// <summary>
        ///     Draw a straight horizontal line or vertical line
        /// </summary>
        /// <param name="pen">pen</param>
        /// <param name="line">line</param>
        private void DrawLineInternal(IPen pen, Line line)
        {
            if (pen.Thickness.IsNearlyEqual(0)) return;

            if (pen.Thickness.IsNearlyEqual(UnderlineThickness) || pen.Thickness.IsNearlyEqual(StrikethroughThickness))
            {
                if (line.Vertical)
                    throw new NotSupportedException(
                        "Vertical strikethrough or underline text decorations is not supported.");

                // horizontal lines with thickness larger than one are text decorations
                ApplyTextDecorationLineInternal(pen, line);
                return;
            }

            if (pen.Brush is MoveConsoleCaretToPositionBrush moveBrush)
            {
                //todo low: same code is above also
                var head = line.PStart.Transform(Transform).ToPixelPoint();
                if (CurrentClip.ContainsExclusive(head))
                {
                    _pixelBuffer[head] = _pixelBuffer[head].Blend(new Pixel(moveBrush.CaretStyle));
                    _consoleWindowImpl.DirtyRegions.AddRect(
                        CurrentClip.Intersect(new PixelRect(head, new PixelSize(1, 1))));
                }

                return;
            }

            DrawLineInternal(pen, line, RectangleLinePosition.Unknown);
        }

        private void ApplyTextDecorationLineInternal(IPen pen, Line line)
        {
            line = TransformLineInternal(line);

            var rectToRefresh = new PixelRect(line.PStart.ToPixelPoint(), new PixelSize(line.Length, 1));
            PixelRect intersectRect = CurrentClip.Intersect(rectToRefresh);
            if (intersectRect.IsEmpty())
                return;

            PixelPoint head = intersectRect.TopLeft;

            TextDecorationLocation textDecoration = pen.Thickness switch
            {
                UnderlineThickness => TextDecorationLocation.Underline,
                StrikethroughThickness => TextDecorationLocation.Strikethrough,
                _ => throw new ArgumentOutOfRangeException($"Unsupported thickness {pen.Thickness}")
            };

            for (int x = intersectRect.X; x < intersectRect.Right; x++)
            {
                Pixel oldPixel = _pixelBuffer[head];
                var newPixelForeground = new PixelForeground(oldPixel.Foreground.Symbol,
                    oldPixel.Foreground.Color,
                    oldPixel.Foreground.Weight,
                    oldPixel.Foreground.Style,
                    textDecoration);
                _pixelBuffer[head] = oldPixel.Blend(new Pixel(newPixelForeground));

                head = head.WithX(head.X + 1);
            }

            _consoleWindowImpl.DirtyRegions.AddRect(intersectRect);
        }

        private void FillRectangleWithBrush(IBrush brush, PixelRect pixelRect)
        {
            Pixel solidPixel = default;
            var solidColorBrush = brush as ISolidColorBrush;
            if (solidColorBrush != null)
            {
                Color solidColor = solidColorBrush.Color;
                if (solidColor is { A: 0 }) return;

                solidPixel = new Pixel(new PixelBackground(solidColor));
            }

            // fill rectangle with brush
            PixelRect targetRect = CurrentClip.Intersect(pixelRect);

            if (targetRect.IsEmpty())
                return;

            // Clamp to valid range to prevent out-of-bounds errors
            ushort gradiantWidth = (ushort)Math.Max(1, pixelRect.Width);
            ushort gradiantHeight = (ushort)Math.Max(1, pixelRect.Height);
            ushort brushY = (ushort)(targetRect.Y - pixelRect.Y);

            for (ushort y = (ushort)targetRect.Y; y < (ushort)targetRect.Bottom; y++, brushY++)
            {
                ushort brushX = (ushort)(targetRect.X - pixelRect.X);
                for (ushort x = (ushort)targetRect.X; x < targetRect.Right; x++, brushX++)
                {
                    Pixel pixelAbove;
                    if (solidColorBrush == null)
                    {
                        Color backgroundColor =
                            brush.FromPosition(brushX, brushY, gradiantWidth, gradiantHeight);
                        pixelAbove = new Pixel(new PixelBackground(backgroundColor));
                    }
                    else
                    {
                        pixelAbove = solidPixel;
                    }

                    _pixelBuffer[x, y] = _pixelBuffer[x, y].Blend(pixelAbove);
                }
            }

            _consoleWindowImpl.DirtyRegions.AddRect(targetRect);
        }

        /// <summary>
        ///     Draw a rectangle line with corners
        /// </summary>
        /// <param name="pen">pen</param>
        /// <param name="line">line</param>
        /// <param name="linePosition">The relative rectangle line position</param>
        private void DrawLineInternal(IPen pen, Line line, RectangleLinePosition linePosition)
        {
            if (pen.Thickness == 0) return;

            line = TransformLineInternal(line);

            Color? foregroundColor =
                ExtractColorOrNullWithPlatformCheck(pen, out LineStyles lineStyles);
            if (foregroundColor == null)
                return;

            LineStyle lineStyle = linePosition switch
            {
                RectangleLinePosition.Top => lineStyles.Top,
                RectangleLinePosition.Right => lineStyles.Right,
                RectangleLinePosition.Bottom => lineStyles.Bottom,
                RectangleLinePosition.Left => lineStyles.Left,
                _ => LineStyle.SingleLine
            };

            if (lineStyle is LineStyle.Edge or LineStyle.EdgeWide)
                DrawEdgeLine(line, linePosition, lineStyle, (Color)foregroundColor);
            else
                DrawBoxLine(line, lineStyle, (Color)foregroundColor);
        }

        private void DrawBoxLine(Line line, LineStyle lineStyle, Color color)
        {
            if (line.Length == 0)
                return;

            var head = line.PStart.ToPixelPoint();

            byte pattern = line.Vertical ? VerticalStartPattern : HorizontalStartPattern;
            var symbol = new Symbol(GetBoxPatternFromLineStyle(pattern, lineStyle));
            DrawLineSymbolAndMoveHead(ref head, line.Vertical, in symbol, color, 1); //beginning

            pattern = line.Vertical ? VerticalLinePattern : HorizontalLinePattern;
            symbol = new Symbol(GetBoxPatternFromLineStyle(pattern, lineStyle));
            DrawLineSymbolAndMoveHead(ref head, line.Vertical, in symbol, color, line.Length - 1); //line

            pattern = line.Vertical ? VerticalEndPattern : HorizontalEndPattern;
            symbol = new Symbol(GetBoxPatternFromLineStyle(pattern, lineStyle));
            DrawLineSymbolAndMoveHead(ref head, line.Vertical, in symbol, color, 1); //ending
        }

        private void DrawEdgeLine(Line line, RectangleLinePosition linePosition, LineStyle lineStyle, Color color)
        {
            if (line.Length == 0)
                return;
            Symbol startSymbol;
            Symbol middleSymbol;
            Symbol endSymbol;
            int iStyle = lineStyle == LineStyle.Edge ? 0 : 1;

            switch (linePosition)
            {
                case RectangleLinePosition.Left:
                    startSymbol = new Symbol(EdgeCornerChars[iStyle][TopLeft]);
                    middleSymbol = new Symbol(EdgeChars[iStyle][(int)RectangleLinePosition.Left]);
                    endSymbol = new Symbol(EdgeCornerChars[iStyle][BottomLeft]);
                    break;
                case RectangleLinePosition.Top:
                    startSymbol = new Symbol(EdgeCornerChars[iStyle][TopLeft]);
                    middleSymbol = new Symbol(EdgeChars[iStyle][(int)RectangleLinePosition.Top]);
                    endSymbol = new Symbol(EdgeCornerChars[iStyle][TopRight]);
                    break;
                case RectangleLinePosition.Right:
                    startSymbol = new Symbol(EdgeCornerChars[iStyle][TopRight]);
                    middleSymbol = new Symbol(EdgeChars[iStyle][(int)RectangleLinePosition.Right]);
                    endSymbol = new Symbol(EdgeCornerChars[iStyle][BottomRight]);
                    break;
                case RectangleLinePosition.Bottom:
                    startSymbol = new Symbol(EdgeCornerChars[iStyle][BottomLeft]);
                    middleSymbol = new Symbol(EdgeChars[iStyle][(int)RectangleLinePosition.Bottom]);
                    endSymbol = new Symbol(EdgeCornerChars[iStyle][BottomRight]);
                    break;
                default:
                    throw new NotImplementedException("This shouldn't happen");
            }

            var head = line.PStart.ToPixelPoint();

            int length = line.Length;
            DrawLineSymbolAndMoveHead(ref head, line.Vertical, in startSymbol, color, 1);
            DrawLineSymbolAndMoveHead(ref head, line.Vertical, in middleSymbol, color, length - 1);
            DrawLineSymbolAndMoveHead(ref head, line.Vertical, in endSymbol, color, 1);
        }

        /// <summary>
        ///     Transform line coordinates
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        private Line TransformLineInternal(Line line)
        {
            if (!Transform.NoRotation())
                return ConsoloniaPlatform.RaiseNotSupported<Line>(
                    NotSupportedRequestCode.TransformLineWithRotationNotSupported, this, line);

            line = (Line)line.WithTransform(Transform);
            return line;
        }

        /// <summary>
        ///     Extract color from pen brush
        /// </summary>
        /// <param name="pen"></param>
        /// <param name="lineStyles"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static Color? ExtractColorOrNullWithPlatformCheck(IPen pen, out LineStyles lineStyles)
        {
            lineStyles = null;
            if (pen is not
                {
                    Brush: LineBrush or ISolidColorBrush
                    // Thickness: 1,
                    // DashStyle: null or { Dashes.Count: 0 },
                    //LineCap: PenLineCap.Flat,
                    //LineJoin: PenLineJoin.Miter
                })
            {
                (Color? color, lineStyles) = ConsoloniaPlatform.RaiseNotSupported<(Color?, LineStyles)>(
                    NotSupportedRequestCode.ExtractColorFromPenNotSupported, null, pen);
                return color;
            }

            IBrush brush = pen.Brush;

            if (brush is LineBrush lineBrush)
            {
                lineStyles = lineBrush.LineStyle;
                brush = lineBrush.Brush;
            }
            else
            {
                lineStyles = new LineStyles();
            }

            return ((ISolidColorBrush)brush).Color;
        }


        private static byte GetBoxPatternFromLineStyle(byte pattern, LineStyle lineStyle)
        {
            switch (lineStyle)
            {
                case LineStyle.SingleLine:
                    return pattern;
                case LineStyle.DoubleLine:
                    return (byte)((pattern << 4) | pattern);
                case LineStyle.Bold:
                    return BoxPattern.BoldPattern;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lineStyle), lineStyle, null);
            }
        }

        /// <summary>
        ///     Draw a line symbol and move the head position.
        /// </summary>
        /// <remarks>
        ///     This moves the head position by count in the appropriate direction.
        /// </remarks>
        /// <param name="head">starting point IN PIXELPOINT COORDINATES THAT CAN BE NEGATIVE</param>
        /// <param name="isVertical">is vertical or horizontal head advancement</param>
        /// <param name="symbol">symbol to draw with</param>
        /// <param name="lineColor">color to use</param>
        /// <param name="count">number of symbols to draw</param>
        private void DrawLineSymbolAndMoveHead(ref PixelPoint head, bool isVertical, in Symbol symbol, Color lineColor,
            int count)
        {
            PixelRect lineBounds = isVertical
                ? new PixelRect(head.X, head.Y, 1, count)
                : new PixelRect(head.X, head.Y, count, 1);
            PixelRect intersectLine = CurrentClip.Intersect(lineBounds);
            if (intersectLine.IsEmpty())
            {
                if (isVertical)
                    head = head.WithY(head.Y + count);
                else
                    head = head.WithX(head.X + count);
                return;
            }

            ushort lineStart = isVertical ? (ushort)intersectLine.Y : (ushort)intersectLine.X;
            ushort lineEnd = isVertical ? (ushort)intersectLine.Bottom : (ushort)intersectLine.Right;
            // adjust to the start of the intersected line
            if (isVertical)
                head = head.WithY(lineStart);
            else
                head = head.WithX(lineStart);

            var newPixel = new Pixel(new PixelForeground(symbol, lineColor));
            for (ushort i = lineStart; i < lineEnd; i++)
            {
                _pixelBuffer[head] = _pixelBuffer[head].Blend(newPixel);

                if (isVertical)
                    head = head.WithY(head.Y + 1);
                else
                    head = head.WithX(head.X + 1);
            }

            _consoleWindowImpl.DirtyRegions.AddRect(intersectLine);
        }
    }
}