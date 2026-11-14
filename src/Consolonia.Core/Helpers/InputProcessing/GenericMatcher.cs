using System;
using System.Collections.Generic;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Matches everything
    /// </summary>
    public class GenericMatcher<TKey>(Action<TKey> onComplete, Func<TKey, bool> filter = null)
        : MatcherWithComplete<TKey, TKey>(onComplete)
    {
        private readonly List<TKey> _accumulatedKeys = [];

        public override AppendResult Append(TKey input)
        {
            if (filter != null && !filter(input))
                return AppendResult.NoMatch;

            _accumulatedKeys.Add(input);
            return AppendResult.Match;
        }

        public override AppendResult TryFlush()
        {
            if (_accumulatedKeys.Count == 0)
                return AppendResult.NoMatch;

            foreach (TKey key in _accumulatedKeys)
                Complete(key);
            _accumulatedKeys.Clear();
            return AppendResult.AutoFlushed;
        }

        public override void Reset()
        {
            _accumulatedKeys.Clear();
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} {{{GetAccumulatedData()}";

            string GetAccumulatedData()
            {
                return _accumulatedKeys.Count == 0 ? "_" : string.Join(", ", _accumulatedKeys);
            }
        }
    }
}