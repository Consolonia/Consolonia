namespace Consolonia.Controls
{
    public interface IConsoleCapabilities
    {
        /// <summary>
        ///     This is true if console supports composing multiple emojis together (like: 👨‍👩‍👧‍👦).
        /// </summary>
        bool SupportsComplexEmoji { get; }
    }
}