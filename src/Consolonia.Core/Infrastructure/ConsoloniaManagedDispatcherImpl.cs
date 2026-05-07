using Avalonia.Controls.Platform;

namespace Consolonia.Core.Infrastructure
{
    internal class ConsoloniaManagedDispatcherImpl : ManagedDispatcherImpl
    {
        private readonly BetterSleepLoopRenderTimer _renderTimer;

        public ConsoloniaManagedDispatcherImpl(BetterSleepLoopRenderTimer renderTimer)
            : base(null)
        {
            _renderTimer = renderTimer;
            Signaled += OnSignaled;
            Timer += OnTimer;
        }

        private void OnSignaled()
        {
            _renderTimer.TriggerTick();
        }

        private void OnTimer()
        {
            _renderTimer.TriggerTick();
        }
    }
}