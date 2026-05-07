using System;
using System.Diagnostics;
using System.Threading;
using Avalonia.Rendering;

namespace Consolonia.Core.Infrastructure
{
    internal class BetterSleepLoopRenderTimer : IRenderTimer, IDisposable
    {
        private Action<TimeSpan> _tick;
        private int _count;
        private readonly object _lock = new();
        private volatile bool _running;
        private volatile bool _disposed;
        private readonly Stopwatch _st = Stopwatch.StartNew();
        private readonly ManualResetEventSlim _wakeup = new(false);
        private readonly ManualResetEventSlim _loopExited = new(false);

        public event Action<TimeSpan> Tick
        {
            add
            {
                lock (_lock)
                {
                    _tick += value;
                    _count++;
                    if (_running)
                        return;
                    _running = true;
                    new Thread(LoopProc) { IsBackground = true }.Start();
                }
            }
            remove
            {
                lock (_lock)
                {
                    _tick -= value;
                    _count--;
                }
            }
        }

        public bool RunsInBackground => true;

        /// <summary>
        /// Requests the render timer to fire a tick as soon as possible.
        /// </summary>
        public void TriggerTick()
        {
            if (!_disposed)
                _wakeup.Set();
        }

        public void Dispose()
        {
            bool wasRunning;
            lock (_lock)
            {
                _disposed = true;
                wasRunning = _running;
            }

            _wakeup.Set();
            if (wasRunning)
                _loopExited.Wait(TimeSpan.FromSeconds(5));
            _wakeup.Dispose();
            _loopExited.Dispose();
        }

        private void LoopProc()
        {
            while (true)
            {
                try
                {
                    _wakeup.Wait(Timeout.Infinite);
                    _wakeup.Reset();
                }
                catch (ObjectDisposedException)
                {
                    _loopExited.Set();
                    return;
                }

                if (_disposed)
                {
                    _loopExited.Set();
                    return;
                }

                lock (_lock)
                {
                    if (_count == 0)
                    {
                        _running = false;
                        _loopExited.Set();
                        return;
                    }
                }

                _tick?.Invoke(_st.Elapsed);
            }
        }
    }
}