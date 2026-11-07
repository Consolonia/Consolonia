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

            _ = _accumulator.Append(rune);
            string candidate = _accumulator.ToString();

            if (_sequences.TryGetValue(candidate, out TResult result1))
            {
                _accumulator.Clear();
                Complete(result1);
                return AppendResult.AutoFlushed;
            }

            if (_prefixes.Contains(candidate))
                return AppendResult.Match;

            _accumulator.Clear();
            return AppendResult.NoMatch;
        }

        public override AppendResult TryFlush()
        {
            return _accumulator.Length > 0 ? AppendResult.Match : AppendResult.NoMatch;
        }

        public override void Reset()
        {
            _accumulator.Clear();
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} {{{(_accumulator.Length == 0 ? "_" : _accumulator.ToString())}}}";
        }
    }
}