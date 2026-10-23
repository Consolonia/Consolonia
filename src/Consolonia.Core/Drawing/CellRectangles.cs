using System;

namespace Consolonia.Core.Drawing
{
    /// <summary>Scratch arrays of one flag per cell, reused across frames.</summary>
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
    }
}