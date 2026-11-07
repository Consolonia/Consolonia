using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Consolonia.Core.Helpers.InputProcessing
{
    public class RegexMatcher<T>(
        Action<(string, T[])> onComplete,
        Func<T, Rune> toRune,
        string regex,
        int? autoFlushOnLength = null)
        : MatcherWithComplete<T, (string, T[])>(onComplete)
    {
        private readonly List<T> _accumulatedKeys = [];
        private readonly StringBuilder _accumulator = new();

        private readonly Regex _regex = new(regex);

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);

            _accumulator.Append(rune);

            string accumulatedString = _accumulator.ToString();
            AppendResult matchResultInternal = MatchResultInternal(accumulatedString);
            if (matchResultInternal != AppendResult.NoMatch)
            {
                _accumulatedKeys.Add(input);

                if (autoFlushOnLength != null && _accumulator.Length == (int)autoFlushOnLength)
                {
                    Complete((accumulatedString, _accumulatedKeys.ToArray()));
                    Reset();
                    return AppendResult.AutoFlushed;
                }
            }
            else
            {
                _accumulator.Length-=rune.Utf16SequenceLength;
            }

            return matchResultInternal;
        }


        private AppendResult MatchResultInternal(string toTest)
        {
            return _regex.IsMatch(toTest) ? AppendResult.Match : AppendResult.NoMatch;
        }

        public override AppendResult TryFlush()
        {
            if (_accumulator.Length == 0) return AppendResult.NoMatch;

            if (autoFlushOnLength != null)
                return AppendResult.Match;

            string currentAccumulated = _accumulator.ToString();
            bool matches = MatchResultInternal(currentAccumulated) == AppendResult.Match;
            if (matches)
            {
                Complete((currentAccumulated, _accumulatedKeys.ToArray()));
                Reset();
                return AppendResult.AutoFlushed;
            }

            return AppendResult.NoMatch;
        }

        private protected int GetAccumulatedLength()
        {
            return _accumulator.Length;
        }

        public override void Reset()
        {
            _accumulator.Clear();
            _accumulatedKeys.Clear();
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} [{regex}, max {autoFlushOnLength}] {{{GetAccumulatedData()}";

            string GetAccumulatedData()
            {
                return _accumulatedKeys.Count == 0 ? "_" : string.Join(", ", _accumulatedKeys);
            }
        }
    }
}