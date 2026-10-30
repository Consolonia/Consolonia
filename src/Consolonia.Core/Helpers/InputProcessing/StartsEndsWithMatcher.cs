using System;
using System.Text;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Matches a sequence of characters that starts with a given string and ends with another given string.
    /// </summary>
    public class StartsEndsWithMatcher<T>(
        Action<string> onComplete,
        Func<T, Rune> toRune,
        string startsWith,
        string endsWith)
        : MatcherWithComplete<T, string>(onComplete)
    {
        private readonly StringBuilder _accumulator = new();

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);

            AppendResult matchResultInternal = MatchResultInternal(_accumulator.ToString() + rune);
            if (matchResultInternal == AppendResult.Match) _accumulator.Append(rune);

            return matchResultInternal;
        }

        private AppendResult MatchResultInternal(string toTest)
        {
            bool startsWithTrue = toTest.StartsWith(startsWith, StringComparison.Ordinal);

            if (startsWithTrue && toTest.EndsWith(endsWith, StringComparison.Ordinal))
            {
                string output = toTest.Substring(startsWith.Length,
                    Math.Max(0, toTest.Length - startsWith.Length - endsWith.Length));

                Complete(output);
                _accumulator.Clear();
                return AppendResult.AutoFlushed;
            }

            if (startsWithTrue || startsWith.StartsWith(toTest, StringComparison.Ordinal)) return AppendResult.Match;

            _accumulator.Clear();

            return AppendResult.NoMatch;
        }

        public override bool TryFlush()
        {
            return _accumulator.Length != 0;
            // if any input, always lying that we are flushed to
            // indicate others should not flush 15F2A2C4-218D-4B4D-86CE-330A312EF6A6
        }

        public override void Reset()
        {
            // because of 15F2A2C4-218D-4B4D-86CE-330A312EF6A6 we are pretending to reset, but we are not buying the lie of the previous tag matcher 
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