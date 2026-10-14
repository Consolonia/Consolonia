using System;
using System.Collections.Generic;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Same as <see cref="GenericMatcher{TKey}" />, but completes with all accumulated keys at once
    /// </summary>
    public class GenericBatchMatcher<TKey>(Action<TKey[]> onComplete, Func<TKey, bool> filter = null)
        : MatcherWithComplete<TKey, TKey[]>(onComplete)
    {
        private readonly List<TKey> _accumulatedKeys = [];

        public override AppendResult Append(TKey input)
        {
            if (filter != null && !filter(input))
                return AppendResult.NoMatch;

            _accumulatedKeys.Add(input);
            return AppendResult.Match;
        }

        public override bool TryFlush()
        {
            if (_accumulatedKeys.Count == 0)
                return false;

            TKey[] keys = [.. _accumulatedKeys];
            _accumulatedKeys.Clear();
            Complete(keys);
            return true;
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