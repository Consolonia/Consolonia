using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    internal class StreamGeometryImpl : IStreamGeometryImpl
    {
        private readonly List<Rectangle> _fills = [];
        private readonly List<Line> _strokes = [];

        public IReadOnlyList<Line> Strokes => _strokes;

        public IReadOnlyList<Rectangle> Fills => _fills;
        // private SKPath _path;

        public Rect Bounds { get; private set; }

        public double ContourLength => _strokes.Sum(l => l.ContourLength);


        public IStreamGeometryImpl Clone()
        {
            var cloneGeometry = new StreamGeometryImpl();
            foreach (Line cloneLine in _strokes.Select(l => new Line(l.PStart, l.PEnd, this, l.Transform)))
                cloneGeometry._strokes.Add(cloneLine);
            foreach (Rectangle cloneRect in _fills.Select(r => new Rectangle(r.Rect, r, r.Transform)))
                cloneGeometry._fills.Add(cloneRect);
            cloneGeometry.Bounds = Bounds;
            return cloneGeometry;
        }

        public bool FillContains(Point point)
        {
            return _fills.Any(rect => rect.FillContains(point));
        }

        public Rect GetRenderBounds(IPen pen)
        {
            Rect strokeBounds = _strokes.Aggregate(new Rect(), (rect, line) => rect.Union(line.GetRenderBounds(pen)));
            return _fills.Aggregate(strokeBounds, (rect, r) => rect.Union(r.GetRenderBounds(pen)));
        }

        public IGeometryImpl GetWidenedGeometry(IPen pen)
        {
            // TODO
            throw new NotImplementedException();
        }

        public IGeometryImpl Intersect(IGeometryImpl geometry)
        {
            return geometry.Intersect(this);
        }

        public IStreamGeometryContextImpl Open()
        {
            return new StreamGeometryContextImpl(this);
        }

        public bool StrokeContains(IPen pen, Point point)
        {
            return _strokes.Any(line => line.StrokeContains(pen, point));
        }

        public bool TryGetPointAndTangentAtDistance(double distance, out Point point, out Point tangent)
        {
            throw new NotImplementedException();
        }

        public bool TryGetPointAtDistance(double distance, out Point point)
        {
            throw new NotImplementedException();
        }

        public bool TryGetSegment(double startDistance, double stopDistance, bool startOnBeginFigure,
            [NotNullWhen(true)] out IGeometryImpl segmentGeometry)
        {
            throw new NotImplementedException();
        }

        public ITransformedGeometryImpl WithTransform(Matrix transform)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     A Consolonia implementation of a <see cref="IStreamGeometryContextImpl" />.
        /// </summary>
        private class StreamGeometryContextImpl : IStreamGeometryContextImpl
        {
            private readonly StreamGeometryImpl _geometryImpl;
            private bool _isFilled;
            private Point _lastPoint;

            /// <summary>
            ///     Initializes a new instance of the StreamGeometryContextImpl class.
            ///     <param name="geometryImpl">Geometry to operate on.</param>
            /// </summary>
            public StreamGeometryContextImpl(StreamGeometryImpl geometryImpl)
            {
                _geometryImpl = geometryImpl;
            }

            /// <inheritdoc />
            public void LineTo(Point point, bool isStroked)
            {
                LineTo(point);
            }

            /// <inheritdoc />
            public void ArcTo(Point point, Size size, double rotationAngle, bool isLargeArc,
                SweepDirection sweepDirection, bool isStroked)
            {
                _lastPoint = point;
            }

            /// <inheritdoc />
            public void CubicBezierTo(Point point1, Point point2, Point point3, bool isStroked)
            {
                ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.CubicBezierToNotSupported);
            }

            /// <inheritdoc />
            public void QuadraticBezierTo(Point point1, Point point2, bool isStroked)
            {
                ConsoloniaPlatform.RaiseNotSupported(NotSupportedRequestCode.QuadraticBezierToNotSupported);
            }

            /// <inheritdoc />
            public void BeginFigure(Point startPoint, bool isFilled)
            {
                _isFilled = isFilled;
                _lastPoint = startPoint;
            }

            public void LineTo(Point point)
            {
                // our strokes are oriented from UpperLeft corner to Right or Down
                if (_lastPoint.X > point.X || _lastPoint.Y > point.Y)
                    _geometryImpl._strokes.Add(new Line(point, _lastPoint));
                else
                    _geometryImpl._strokes.Add(new Line(_lastPoint, point));
                _lastPoint = point;
            }

            public void EndFigure(bool isClosed)
            {
                Rect bound = _geometryImpl._strokes.Aggregate(new Rect(), (rect, line) => rect.Union(line.Bounds));
                _geometryImpl.Bounds = bound;
                if (_isFilled)
                {
                    // Process vertical and horizontal strokes to determine fill rectangles
                    List<Rect> fillRectangles =
                        ProcessRectilinearStrokesToFillRectangles(_geometryImpl._strokes, bound);
                    foreach (Rect rect in fillRectangles) _geometryImpl._fills.Add(new Rectangle(rect, _geometryImpl));
                }
            }

            /// <inheritdoc />
            public void SetFillRule(FillRule fillRule)
            {
            }

            public void Dispose()
            {
            }

            /// <summary>
            ///     Processes rectilinear (only horizontal and vertical) strokes to determine fill rectangles
            /// </summary>
            /// <param name="strokes">Collection of horizontal and vertical strokes</param>
            /// <param name="bounds">Bounding rectangle of all strokes</param>
            /// <returns>Collection of rectangles that represent the filled area</returns>
            private static List<Rect> ProcessRectilinearStrokesToFillRectangles(IReadOnlyList<Line> strokes,
                Rect bounds)
            {
                var fillRects = new List<Rect>();

                if (strokes.Count == 0 || bounds.IsEmpty())
                    return fillRects;

                // For simple rectangular shapes (most common case)
                if (IsSimpleRectangle(strokes, bounds))
                {
                    // Create a rectangle that fills the interior (excluding the stroke boundary)
                    Rect interiorRect = bounds;
                    //new Rect(
                    //    bounds.X + 1,                                                                          
                    //    bounds.Y + 1,
                    //    Math.Max(0, bounds.Width - 2),
                    //    Math.Max(0, bounds.Height - 2));

                    if (!interiorRect.IsEmpty())
                        fillRects.Add(interiorRect);

                    return fillRects;
                }

                // For complex rectilinear polygons, use horizontal scanline approach
                return HorizontalScanlineFill(strokes, bounds);
            }

            /// <summary>
            ///     Determines if the strokes form a simple rectangle
            /// </summary>
            private static bool IsSimpleRectangle(IReadOnlyList<Line> strokes, Rect bounds)
            {
                if (strokes.Count != 4)
                    return false;

                List<Line> horizontalLines = strokes.Where(s => !s.Vertical).ToList();
                List<Line> verticalLines = strokes.Where(s => s.Vertical).ToList();

                if (horizontalLines.Count != 2 || verticalLines.Count != 2)
                    return false;

                // Check if we have top, bottom, left, and right edges
                bool hasTop = horizontalLines.Any(l => Math.Abs(l.PStart.Y - bounds.Top) < 0.5);
                bool hasBottom = horizontalLines.Any(l => Math.Abs(l.PStart.Y - bounds.Bottom) < 0.5);
                bool hasLeft = verticalLines.Any(l => Math.Abs(l.PStart.X - bounds.Left) < 0.5);
                bool hasRight = verticalLines.Any(l => Math.Abs(l.PStart.X - bounds.Right) < 0.5);

                return hasTop && hasBottom && hasLeft && hasRight;
            }

            /// <summary>
            ///     Fills rectilinear polygon using horizontal scanlines
            /// </summary>
            private static List<Rect> HorizontalScanlineFill(IReadOnlyList<Line> strokes, Rect bounds)
            {
                var fillRects = new List<Rect>();

                // Get all vertical edges (these will determine fill spans)
                List<Line> verticalEdges = strokes.Where(s => s.Vertical).ToList();

                if (verticalEdges.Count == 0)
                    return fillRects;

                // Process each integer Y coordinate within bounds
                for (int y = (int)Math.Ceiling(bounds.Top); y < (int)Math.Floor(bounds.Bottom); y++)
                {
                    var intersections = new List<double>();

                    // Find X coordinates where vertical edges intersect current scanline
                    foreach (Line edge in verticalEdges)
                    {
                        // Check if this vertical edge spans the current Y coordinate
                        double yMin = Math.Min(edge.PStart.Y, edge.PEnd.Y);
                        double yMax = Math.Max(edge.PStart.Y, edge.PEnd.Y);

                        if (y >= yMin && y < yMax) intersections.Add(edge.PStart.X); // Vertical line has constant X
                    }

                    // Sort intersections and create horizontal fill spans
                    intersections.Sort();

                    // Remove duplicates (in case of overlapping edges)
                    intersections = intersections.Distinct().ToList();

                    // Pair up intersections to create fill spans (inside-outside rule)
                    for (int i = 0; i < intersections.Count - 1; i += 2)
                        if (i + 1 < intersections.Count)
                        {
                            double xStart = intersections[i];
                            double xEnd = intersections[i + 1];

                            // Create fill rectangle for this span, excluding the boundary
                            double fillXStart = xStart;
                            double fillWidth = xEnd - fillXStart;

                            if (fillWidth > 0) fillRects.Add(new Rect(fillXStart, y, fillWidth, 1));
                        }
                }

                return fillRects;
            }
        }
    }
}
