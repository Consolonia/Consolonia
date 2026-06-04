using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;

namespace Consolonia.Core.Infrastructure
{
    public sealed class AsyncDataTransfer : IAsyncDataTransfer
    {
        private readonly List<IAsyncDataTransferItem> _items;
        private bool _disposedValue;

        public AsyncDataTransfer()
        {
            _items = new List<IAsyncDataTransferItem>();
        }

        public AsyncDataTransfer(IAsyncDataTransferItem item)
        {
            _items = new List<IAsyncDataTransferItem> { item };
        }

        public AsyncDataTransfer(IEnumerable<IAsyncDataTransferItem> items)
        {
            _items = items.ToList();
        }

        public IReadOnlyList<DataFormat> Formats => _items.SelectMany(i => i.Formats)
            .Distinct()
            .ToList()
            .AsReadOnly();

        public IReadOnlyList<IAsyncDataTransferItem> Items => _items.AsReadOnly();

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

    public class AsyncDataTransferItem : IAsyncDataTransferItem
    {
        private readonly List<DataFormat> _formats;
        private readonly object _item;

        public AsyncDataTransferItem(object item, params DataFormat[] formats)
        {
            _formats = formats.ToList();
            _item = item;
        }

        #region IAsyncDataTransferItem Members

        public IReadOnlyList<DataFormat> Formats => _formats;

        public Task<object> TryGetRawAsync(DataFormat format)
        {
            if (_formats.Contains(format))
                return Task.FromResult(_item);
            return Task.FromResult<object>(null);
        }

        #endregion

        #region Synchronous format helpers

        public bool Contains(string dataFormat)
        {
            return _formats.Any(f => f.Identifier == dataFormat);
        }

        public object Get(string dataFormat)
        {
            if (Contains(dataFormat))
                return _item;
            return null;
        }

        public IEnumerable<string> GetDataFormats()
        {
            return _formats.Select(f => f.Identifier);
        }

        #endregion
    }
}