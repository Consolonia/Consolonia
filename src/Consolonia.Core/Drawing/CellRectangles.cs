using System;

namespace Consolonia.Core.Drawing
{
    /// <summary>Whether a cell joins the rectangle being grown from (x, y), at offset (dx, dy).</summary>
    internal interface IRectangleCells
    {
        bool Joins(int x, int y, int dx, int dy);
    }

    /// <summary>Grows maximal rectangles of cells over a buffer, and keeps the scratch arrays that takes.</summary>
    internal static class CellRectangles
    {
        /// <summary>A cleared scratch array of at least <paramref name="length" />, reused across frames.</summary>
        public static bool[] Scratch(ref bool[] scratch, int length)
        {
            if (scratch == null || scratch.Length < length)
                scratch = new bool[length];
            else
                Array.Clear(scratch, 0, length);
            return scratch;
        }

        /// <summary>
        ///     Grows a rectangle of cells from (x, y): rightward while cells join, then downward row by row,
        ///     never into a cell already <paramref name="visited" />, and marks the cells it takes as visited.
        /// </summary>
        /// <param name="cells">Which cells join the rectangle.</param>
        /// <param name="visited">One flag per cell of the buffer, row by row.</param>
        /// <param name="bufferWidth">The width of the buffer in cells.</param>
        /// <param name="bufferHeight">The height of the buffer in cells.</param>
        /// <param name="x">The column of the cell the rectangle grows from.</param>
        /// <param name="y">The row of the cell the rectangle grows from.</param>
        /// <param name="narrowToShortRows">
        ///     A row that joins for only part of the width narrows the rectangle to it; otherwise such a row
        ///     ends the rectangle.
        /// </param>
        public static (int Width, int Height) GrowRectangle<TCells>(TCells cells, bool[] visited,
            int bufferWidth, int bufferHeight, int x, int y, bool narrowToShortRows)
            where TCells : struct, IRectangleCells
        {
            int width = 1;
            while (x + width < bufferWidth && !visited[y * bufferWidth + x + width] &&
                   cells.Joins(x, y, width, 0))
                width++;

            int height = 1;
            while (y + height < bufferHeight)
            {
                int rowStart = (y + height) * bufferWidth + x;
                int rowWidth = 0;
                while (rowWidth < width && !visited[rowStart + rowWidth] && cells.Joins(x, y, rowWidth, height))
                    rowWidth++;

                if (rowWidth == 0 || rowWidth < width && !narrowToShortRows)
                    break;
                width = rowWidth;
                height++;
            }

            for (int row = 0; row < height; row++)
                visited.AsSpan((y + row) * bufferWidth + x, width).Fill(true);

            return (width, height);
        }
    }
}