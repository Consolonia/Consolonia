using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class PlatformClipboardAdapterTests
    {
        [Test]
        public async Task OwnedDataIsAvailableOnlyWhileClipboardOwnsIt()
        {
            var implementation = new OwnedClipboard();
            IClipboard clipboard = PlatformSupportExtensions.CreateClipboard(implementation);
            var data = new AsyncDataTransfer(new AsyncDataTransferItem("text", DataFormat.Text));

            await clipboard.SetDataAsync(data);
            Assert.AreSame(data, await clipboard.TryGetDataAsync());
            Assert.AreSame(data, await clipboard.TryGetInProcessDataAsync());

            implementation.IsOwner = false;
            Assert.IsNull(await clipboard.TryGetInProcessDataAsync());

            implementation.IsOwner = true;
            Assert.IsNull(await clipboard.TryGetInProcessDataAsync());
        }

        [Test]
        public async Task ClearingAndFlushingDelegateToPlatformClipboard()
        {
            var implementation = new OwnedClipboard();
            IClipboard clipboard = PlatformSupportExtensions.CreateClipboard(implementation);
            await clipboard.SetDataAsync(new AsyncDataTransfer(new AsyncDataTransferItem("text", DataFormat.Text)));

            await clipboard.FlushAsync();
            Assert.IsTrue(implementation.WasFlushed);

            await clipboard.SetDataAsync(null);
            Assert.IsTrue(implementation.WasCleared);
            Assert.IsNull(await clipboard.TryGetInProcessDataAsync());
            Assert.IsNull(await clipboard.TryGetDataAsync());
        }

        [Test]
        public async Task NonOwnedClipboardHasNoInProcessDataOrFlushRequirement()
        {
            IClipboard clipboard = PlatformSupportExtensions.CreateClipboard(new ConsoleClipboard());
            await clipboard.SetDataAsync(new AsyncDataTransfer(new AsyncDataTransferItem("text", DataFormat.Text)));

            Assert.IsNull(await clipboard.TryGetInProcessDataAsync());
            Assert.AreEqual("text", await (await clipboard.TryGetDataAsync()).TryGetTextAsync());
            Assert.DoesNotThrowAsync(async () => await clipboard.FlushAsync());
        }

        private sealed class OwnedClipboard : IOwnedClipboardImpl, IFlushableClipboardImpl
        {
            private IAsyncDataTransfer _data;

            public bool IsOwner { get; set; } = true;
            public bool WasCleared { get; private set; }
            public bool WasFlushed { get; private set; }

            public Task FlushAsync()
            {
                WasFlushed = true;
                return Task.CompletedTask;
            }

            public Task<bool> IsCurrentOwnerAsync()
            {
                return Task.FromResult(IsOwner);
            }

            public Task<IAsyncDataTransfer> TryGetDataAsync()
            {
                return Task.FromResult(_data);
            }

            public Task SetDataAsync(IAsyncDataTransfer dataTransfer)
            {
                _data = dataTransfer;
                return Task.CompletedTask;
            }

            public Task ClearAsync()
            {
                _data = null;
                WasCleared = true;
                return Task.CompletedTask;
            }
        }
    }
}