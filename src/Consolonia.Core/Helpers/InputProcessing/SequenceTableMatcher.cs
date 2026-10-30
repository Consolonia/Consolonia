using System;
using System.Collections.Generic;
using System.Text;

namespace Consolonia.Core.Helpers.InputProcessing
{
    public class SequenceTableMatcher<TInput, TResult> : MatcherWithComplete<TInput, TResult>
    {
        private readonly StringBuilder _accumulator = new();
        private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TResult> _sequences = new(StringComparer.Ordinal);
        private readonly Func<TInput, Rune> _toRune;

        public SequenceTableMatcher(Action<TResult> onComplete, Func<TInput, Rune> toRune,
            IEnumerable<(string sequence, TResult result)> table)
            : base(onComplete)
        {
            _toRune = toRune;

            foreach ((string sequence, TResult result) in table)
            {
                if (!_sequences.TryAdd(sequence, result))
                {
                    throw new ArgumentException($"Sequence '{sequence}' already exists in the table.");
                }

                for (int length = 1; length < sequence.Length; length++)
                    _ = _prefixes.Add(sequence[..length]);
            }
        }

        public override AppendResult Append(TInput input)
        {
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
            /* Claim the input only once it is unambiguously on its way to a sequence from the table. A lone escape
               is not: it may still turn out to be a solo escape or the leading half of an ALT+combination, and the
               matchers below have to be allowed to keep collecting it - claiming it here makes InputProcessor
               reset them, and the combination degenerates into the bare letter. */
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
