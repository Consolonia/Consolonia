using System;
using System.Diagnostics;
using System.Threading;
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

        private Action<TimeSpan>? _tick;
        private Thread? _thread;
        private bool _disposed;

        public event Action<TimeSpan> Tick
        {
            add
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);

                    _tick += value;

                    if (_thread != null)
                        return;

                    _thread = new Thread(LoopProc)
                    {
                        IsBackground = true,
                        Name = nameof(BetterSleepLoopRenderTimer)
                    };

                    _thread.Start();
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
            Thread thread;

            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                thread = _thread;
                _wakeup.Set();
            }

            if (thread != null && thread.Join(TimeSpan.FromSeconds(5)))
                _wakeup.Dispose();
        }

        private void LoopProc()
        {
            while (true)
            {
                try
                {
                    _wakeup.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Action<TimeSpan>? tick;

                lock (_lock)
                {
                    if (_disposed || _tick == null)
                    {
                        _thread = null;
                        return;
                    }

                    tick = _tick;
                }

                tick(_st.Elapsed);
            }
        }
    }
}