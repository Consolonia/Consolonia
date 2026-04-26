namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Marker registered in AvaloniaLocator when Sixel framebuffer mode is active.
    ///     ConsoleWindowImpl checks for this to report pixel-based ClientSize
    ///     instead of character-cell-based ClientSize.
    /// </summary>
    internal sealed class SixelMode
    {
        public static SixelMode Instance { get; } = new();
    }
}
