using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Consolonia.Controls;

namespace Consolonia.Core.Helpers
{
    public sealed class FastBuffer<T>(Func<T[]> readDataFunction) : IDisposable
    {
        private readonly object _lock = new();
        private readonly ManualResetEvent _manualResetEvent = new(false);
        private readonly Queue<T> _queue = new(1);
        private readonly object _readingLock = new();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            //todo: low return when loop exited
            //todo: low will not unblock reading or writing threads.
        }

        public void StartReading()
        {
            Task _ = Task.Run(async () =>
            {
                while (!_disposed)
                    try
                    {
                        T[] newData = readDataFunction();
                        if (!newData.Any())
                            return;

                        Enqueue(newData);
                    }
                    catch (Exception exception)
                    {
                        await Helper.WaitDispatcherInitialized();
                        Dispatcher.UIThread.Post(
                            () => throw new ConsoloniaException("Exception in input loop", exception),
                            DispatcherPriority.MaxValue);
                    }
            });
        }

        private void Enqueue(IReadOnlyCollection<T> items)
        {
            lock (_lock)
            {
                foreach (T item in items)
                    _queue.Enqueue(item);

                _manualResetEvent.Set();
            }
        }

        public T[] Dequeue()
        {
            lock (_readingLock)
            {
                bool waitOne = false;

                lock (_lock)
                {
                    if (_queue.Count == 0) waitOne = true;
                }

                if (waitOne)
                    _manualResetEvent.WaitOne();

                lock (_lock)
                {
                    T[] result = [.. _queue];
                    _queue.Clear();
                    _manualResetEvent.Reset();
                    return result;
                }
            }
        }
    }
}