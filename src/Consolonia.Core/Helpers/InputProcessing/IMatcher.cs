namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Interface for a matcher that accumulates input and tries to match it to a pattern.
    /// </summary>
    /// <typeparam name="T">The type of input to match.</typeparam>
    public interface IMatcher<in T>
    {
        AppendResult Append(T input);
        AppendResult TryFlush();
        void Reset();
        string GetDebugInfo();
    }
}