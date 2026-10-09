namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     The cell size in pixels assumed when the terminal does not report one: 8 by 16, the common
    ///     VGA text cell.
    /// </summary>
    internal static class DefaultCellPixelSize
    {
        public const int Width = 8;
        public const int Height = 16;
    }
}
