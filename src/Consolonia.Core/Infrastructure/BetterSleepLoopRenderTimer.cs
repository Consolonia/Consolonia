using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Rendering;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    /// chatGPT 5.5 version, briefly checked
    /// </summary>
    internal sealed class BetterSleepLoopRenderTimer : IRenderTimer, IDisposable
    {
        private readonly object _lock = new();
        private readonly Stopwatch _st = Stopwatch.StartNew();
        private readonly AutoResetEvent _wakeup = new(false);

        private Action<TimeSpan> _tick;
        private bool _disposed;
        private Task _loopTask;

        public event Action<TimeSpan> Tick
        {
            add
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);

                    _tick += value;

                    if (_loopTask != null)
                        return;

                    _loopTask = Task.Run(LoopProc);
                }
            }

            remove
            {
                lock (_lock)
                {
                    _tick -= value;

                    if (_tick == null)
                        _wakeup.Set();
                }
            }
        }

        public bool RunsInBackground => true;

        public void TriggerTick()
        {
            lock (_lock)
            {
                if (!_disposed)
                    _wakeup.Set();
            }
        }

        public void Dispose()
        {
            Task task;
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                task = _loopTask;
                _wakeup.Set();
            }

            task?.Wait();

            _wakeup.Dispose();
        }

        private void LoopProc()
        {
            while (!_disposed)
            {
                try
                {
                    _wakeup.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Action<TimeSpan> tick;

                lock (_lock)
                {
                    if (_tick == null)
                    {
                        _loopTask = null;
                        return;
                    }

                    tick = _tick;
                }

                tick(_st.Elapsed);
            }
        }
    }
}