using System.Collections.Generic;
using Avalonia;
using Consolonia.Core.Drawing;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     A readonly snapshot of rectangles.
    /// </summary>
    internal class Snapshot
    {
        private IReadOnlyList<PixelRect> _rectangles;

        private Snapshot(IReadOnlyList<PixelRect> rectangles)
        {
            _rectangles = rectangles;
        }

        public bool IsEmpty => _rectangles.Count == 0;

        /// <summary>
        ///     Marks every cell the rectangles cover (their right and bottom edges exclusive) in a row-major
        ///     mask <paramref name="width" /> cells wide. The rectangles must lie within the mask (see
        ///     <see cref="Intersect" />).
        /// </summary>
        public void MarkCells(System.Span<bool> cells, int width)
        {
            for (int i = 0; i < _rectangles.Count; i++)
            {
                PixelRect rect = _rectangles[i];
                for (int y = rect.Y; y < rect.Bottom; y++)
                    cells.Slice(y * width + rect.X, rect.Width).Fill(true);
            }
        }

        public void Intersect(int x, int y, ushort width, ushort height)
        {
            var result = new List<PixelRect>();
            foreach (PixelRect rectangle in _rectangles)
            {
                PixelRect intersected = rectangle.Intersect(new PixelRect(x, y, width, height));
                if (!intersected.IsEmpty())
                    result.Add(intersected);
            }

            _rectangles = result.AsReadOnly();
        }

        /// <summary>
        ///     A thread-safe collection of normalized rectangles.
        /// </summary>
        public class Regions
        {
            private readonly List<PixelRect> _rectangles = [];

            public void AddRect(PixelRect rect)
            {
                if (rect.IsEmpty())
                    return;

                lock (_rectangles)
                {
                    //todo: sometimes two rectangles can be replaced by one bigger rectangle if W or H matches
                    for (int i = 0; i < _rectangles.Count; i++)
                    {
                        PixelRect existingRect = _rectangles[i];
                        if (existingRect.Contains(rect)) return;

                        if (rect.Contains(existingRect))
                        {
                            _rectangles.RemoveAt(i);
                            i--;
                        }
                    }

                    _rectangles.Add(rect);
                }
            }

            public Snapshot GetSnapshotAndClear()
            {
                lock (_rectangles)
                {
                    var snapshot = new Snapshot([.. _rectangles]);
                    _rectangles.Clear();
                    return snapshot;
                }
            }
        }
    }
}