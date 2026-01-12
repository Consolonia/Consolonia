using Avalonia;
using Avalonia.Controls.Platform;

namespace Consolonia.ManagedWindows.Storage
{
    public static class StorageStartupExtensions
    {
        public static AppBuilder UseConsoloniaStorage(this AppBuilder builder)
        {
            return builder.With<IStorageProviderFactory>(new ConsoloniaStorageProviderFactory());
        }
    }
}