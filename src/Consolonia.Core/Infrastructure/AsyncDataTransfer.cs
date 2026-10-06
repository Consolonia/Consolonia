using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;

namespace Consolonia.Core.Infrastructure
{
    public sealed class AsyncDataTransfer : IAsyncDataTransfer
    {
        private readonly ReadOnlyCollection<IAsyncDataTransferItem> _items;
        private bool _disposedValue;

        public AsyncDataTransfer()
        {
            _items = new ReadOnlyCollection<IAsyncDataTransferItem>([]);
        }

        public AsyncDataTransfer(IAsyncDataTransferItem item)
        {
            _items = new ReadOnlyCollection<IAsyncDataTransferItem>([item]);
        }

        public AsyncDataTransfer(IEnumerable<IAsyncDataTransferItem> items)
        {
            _items = items.ToArray().AsReadOnly();
        }

        public IReadOnlyList<DataFormat> Formats => _items.SelectMany(i => i.Formats)
            .Distinct()
            .ToArray()
            .AsReadOnly();

        public IReadOnlyList<IAsyncDataTransferItem> Items => _items;

        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                    foreach (IAsyncDataTransferItem item in Items)
                        // ReSharper disable SuspiciousTypeConversion.Global
                        if (item is IDisposable disposable)
                            disposable.Dispose();

                _disposedValue = true;
            }
        }

        ~AsyncDataTransfer()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class AsyncDataTransferItem(object item, params DataFormat[] formats) : IAsyncDataTransferItem
    {
        private readonly ReadOnlyCollection<DataFormat> _formats = formats.AsReadOnly();


        public IReadOnlyList<DataFormat> Formats => _formats;

        public Task<object> TryGetRawAsync(DataFormat format)
        {
            return _formats.Contains(format) ? Task.FromResult(item) : Task.FromResult<object>(null);
        }
    }
}