using Avalonia;
using Avalonia.Controls.Platform;

namespace Consolonia.Core.Infrastructure
{
    public static class StorageStartupExtensions
    {
        public static AppBuilder UseConsoloniaStorage(this AppBuilder builder)
        {
            return builder.With<IStorageProviderFactory>(new ConsoloniaStorageProviderFactory());
        }
    }
}