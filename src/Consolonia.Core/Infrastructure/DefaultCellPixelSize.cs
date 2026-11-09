namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     The cell size in pixels to use when the terminal does not report one. 8x16 is the common
    ///     VGA text cell size.
    /// </summary>
    internal static class DefaultCellPixelSize
    {
        public const int Width = 8;
        public const int Height = 16;
    }
}