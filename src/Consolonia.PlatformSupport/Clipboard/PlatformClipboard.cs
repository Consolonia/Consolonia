using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Consolonia.PlatformSupport.Clipboard
{
    internal sealed class PlatformClipboard : IClipboard
    {
        private readonly IClipboardImpl _implementation;
        private IAsyncDataTransfer _ownedData;

        public PlatformClipboard(IClipboardImpl implementation)
        {
            _implementation = implementation;
        }

        public Task ClearAsync()
        {
            _ownedData?.Dispose();
            _ownedData = null;
            return _implementation.ClearAsync();
        }

        public Task SetDataAsync(IAsyncDataTransfer dataTransfer)
        {
            if (dataTransfer is null)
                return ClearAsync();

            if (_implementation is IOwnedClipboardImpl)
                _ownedData = dataTransfer;

            return _implementation.SetDataAsync(dataTransfer);
        }

        public Task FlushAsync()
        {
            return _implementation is IFlushableClipboardImpl flushable
                ? flushable.FlushAsync()
                : Task.CompletedTask;
        }

        public Task<IAsyncDataTransfer> TryGetDataAsync()
        {
            return _implementation.TryGetDataAsync();
        }

        public async Task<IAsyncDataTransfer> TryGetInProcessDataAsync()
        {
            if (_ownedData is null || _implementation is not IOwnedClipboardImpl owned)
                return null;

            if (!await owned.IsCurrentOwnerAsync())
                _ownedData = null;

            return _ownedData;
        }
    }
}
