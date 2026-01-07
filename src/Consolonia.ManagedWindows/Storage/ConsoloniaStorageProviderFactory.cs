using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform.Storage;

namespace Consolonia.Core.Infrastructure
{
    public class ConsoloniaStorageProviderFactory : IStorageProviderFactory
    {
        public IStorageProvider CreateProvider(TopLevel topLevel)
        {
            return new ConsoloniaStorageProvider();
        }
    }
}