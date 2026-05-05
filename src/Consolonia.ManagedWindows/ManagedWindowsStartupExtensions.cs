using Avalonia;
using Consolonia.Core.Infrastructure;

namespace Consolonia.ManagedWindows
{
    public static class ManagedWindowsStartupExtensions
    {
        /// <summary>
        ///     Enables standard Avalonia Window.ShowDialog() to work by routing secondary
        ///     window creation through ManagedWindow-based IWindowImpl.
        /// </summary>
        public static AppBuilder UseManagedWindows(this AppBuilder builder)
        {
            ConsoloniaPlatform.SecondaryWindowFactory = mainWindow => new ManagedWindowImpl(mainWindow);
            return builder;
        }
    }
}
