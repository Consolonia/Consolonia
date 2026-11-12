using System;
using Avalonia.Logging;
using Consolonia.Core.Helpers.Logging;

namespace Consolonia.Core.Helpers.InputProcessing
{
    public abstract class MatcherWithComplete<T, TComplete> : IMatcher<T>
    {
        private readonly ParametrizedLogger _logger = Log.CreateInputLogger(LogEventLevel.Information);

        protected MatcherWithComplete(Action<TComplete> onComplete)
        {
            OnComplete += onComplete;
        }

        public abstract AppendResult Append(T input);
        public abstract bool TryFlush();
        public abstract void Reset();

        public abstract string GetDebugInfo();

        public event Action<TComplete> OnComplete;

        protected void Complete(TComplete input)
        {
            _logger.Log3("Complete fired for {Matcher} with input: {Input}", GetDebugInfo(), input);
            OnComplete.Invoke(input);
        }
    }
}