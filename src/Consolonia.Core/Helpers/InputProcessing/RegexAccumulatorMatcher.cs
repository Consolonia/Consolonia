using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Common base for matchers that accumulate input runes into a buffer and test the
    ///     buffer against a regex on every append, where the regex matches both complete
    ///     sequences and valid partial prefixes (partial prefixes have no <see cref="TerminatorGroupName" /> group).
    /// </summary>
    public abstract class RegexAccumulatorMatcher<T, TComplete>(
        Action<TComplete> onComplete,
        Func<T, Rune> toRune,
        Regex patternRegex)
        : MatcherWithComplete<T, TComplete>(onComplete)
        where TComplete : struct
    {
        public const string TerminatorGroupName = "terminator";

        /// <summary>
        ///     Called once a match with <see cref="TerminatorGroupName" /> group is found.
        ///     Return <see langword="null" /> to indicate that <paramref name="match" />
        ///     is not actually a match at the end of the day
        ///     Otherwise return a result
        /// </summary>
        protected abstract TComplete? OnTerminatorMatched(Match match);

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);
            Accumulator.Append(rune);

            string current = Accumulator.ToString();

            Match match = PatternRegex.Match(current);
            if (!match.Success)
            {
                Accumulator.Length--;
                if (Accumulator.Length > 0)
                    Accumulator.Clear();

                return AppendResult.NoMatch;
            }

            if (!match.Groups[TerminatorGroupName].Success)
                // Valid prefix, but not complete yet
                return AppendResult.Match;

            TComplete? completed = OnTerminatorMatched(match);
            if (completed is null)
            {
                Accumulator.Clear();
                return AppendResult.NoMatch;
            }

            Complete(completed.Value);
            Accumulator.Clear();
            return AppendResult.AutoFlushed;
        }

        public override void Reset()
        {
            // Intentionally not clearing - same pattern as StartsEndsWithMatcher
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} {{{(Accumulator.Length == 0 ? "_" : Accumulator.ToString())}}}";
        }
#pragma warning disable CA1051
        protected readonly StringBuilder Accumulator = new();
        protected readonly Regex PatternRegex = patternRegex;
#pragma warning restore CA1051
    }
}