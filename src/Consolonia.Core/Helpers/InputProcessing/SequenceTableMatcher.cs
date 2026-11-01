using System;
using System.Collections.Generic;
using System.Text;

namespace Consolonia.Core.Helpers.InputProcessing
{
    public class SequenceTableMatcher<TInput, TResult> : MatcherWithComplete<TInput, TResult>
    {
        private readonly StringBuilder _accumulator = new();
        private readonly Func<TInput, bool> _canMatch;
        private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TResult> _sequences = new(StringComparer.Ordinal);
        private readonly Func<TInput, Rune> _toRune;

        public SequenceTableMatcher(Action<TResult> onComplete, Func<TInput, Rune> toRune,
            IEnumerable<(string sequence, TResult result)> table, Func<TInput, bool> canMatch)
            : base(onComplete)
        {
            _toRune = toRune;
            _canMatch = canMatch;

            foreach ((string sequence, TResult result) in table)
            {
                if (!_sequences.TryAdd(sequence, result))
                    throw new ArgumentException($"Sequence '{sequence}' already exists in the table.");

                for (int length = 1; length < sequence.Length; length++)
                    _ = _prefixes.Add(sequence[..length]);
            }
        }

        public override AppendResult Append(TInput input)
        {
            if (!_canMatch(input))
            {
                _accumulator.Clear();
                return AppendResult.NoMatch;
            }

            Rune rune = _toRune(input);
            int collected = _accumulator.Length;

            AppendResult result = Collect(rune);

            if (result == AppendResult.NoMatch && collected > 0)
                // if was a reset, then adding input once again (so we don't loose first character after reset
                result = Collect(rune);

            return result;
        }

        public override bool TryFlush()
        {
            // (15F2A2C4-218D-4B4D-86CE-330A312EF6A6)
            return _accumulator.Length > 1;
        }

        public override void Reset()
        {
            /* (15F2A2C4-218D-4B4D-86CE-330A312EF6A6). */
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} {{{(_accumulator.Length == 0 ? "_" : _accumulator.ToString())}}}";
        }

        private AppendResult Collect(Rune rune)
        {
            _ = _accumulator.Append(rune);
            string candidate = _accumulator.ToString();

            if (_sequences.TryGetValue(candidate, out TResult result))
            {
                _accumulator.Clear();
                Complete(result);
                return AppendResult.AutoFlushed;
            }

            if (_prefixes.Contains(candidate)) return AppendResult.Match;

            _accumulator.Clear();
            return AppendResult.NoMatch;
        }
    }
}