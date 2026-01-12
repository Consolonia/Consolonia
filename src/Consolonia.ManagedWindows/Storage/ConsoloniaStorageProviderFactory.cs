using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform.Storage;

namespace Consolonia.ManagedWindows.Storage
{
    public class ConsoloniaStorageProviderFactory : IStorageProviderFactory
    {
        public IStorageProvider CreateProvider(TopLevel topLevel)
        {
            return new ConsoloniaStorageProvider();
        }
    }
}