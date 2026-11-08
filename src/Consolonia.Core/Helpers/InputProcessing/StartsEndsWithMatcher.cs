using System;
using System.Text;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Matches a sequence of characters that starts with a given string and ends with another given string.
    /// </summary>
    [Obsolete("Use with caution: only one instance of this matcher can work", false)]
    public class StartsEndsWithMatcher<T>(
        Action<string> onComplete,
        Func<T, Rune> toRune,
        string startsWith,
        string endsWith)
        : MatcherWithComplete<T, string>(onComplete)
    {
        private readonly StringBuilder _accumulator = new();
        private bool _isCapturing;

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);

            _accumulator.Append(rune);
            string accumulated = _accumulator.ToString();

            if (_isCapturing)
            {
                if (accumulated.EndsWith(endsWith, StringComparison.Ordinal))
                {
                    string output = accumulated[..^endsWith.Length];
                    _isCapturing = false;
                    Reset();
                    Complete(output);
                }

                return AppendResult.AutoFlushed;
            }

            if (accumulated == startsWith)
            {
                _accumulator.Clear();
                _isCapturing = true;
                return AppendResult.AutoFlushed;
            }

            if (startsWith.StartsWith(accumulated, StringComparison.Ordinal))
                return AppendResult.Match;

            Reset();
            return AppendResult.NoMatch;
        }

        public override AppendResult TryFlush()
        {
            return _isCapturing || _accumulator.Length > 0
                ? AppendResult.Match
                : AppendResult.NoMatch;
        }

        public override void Reset()
        {
            // AutoFlushed makes the processor reset every matcher, including this one.
            if (_isCapturing)
                return;

            _accumulator.Clear();
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} [{startsWith}...{endsWith}] {{{GetAccumulatedData()}";

            string GetAccumulatedData()
            {
                return _accumulator.Length == 0 ? "_" : _accumulator.ToString();
            }
        }
    }
}