using Avalonia;
using Avalonia.Platform;
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
            builder.AfterPlatformServicesSetup(_ =>
            {
                AvaloniaLocator.CurrentMutable
                    .Bind<IChildWindowImplFactory>()
                    .ToConstant(new ManagedChildWindowFactory());
            });
            return builder;
        }

        private sealed class ManagedChildWindowFactory : IChildWindowImplFactory
        {
            public IWindowImpl CreateChildWindow(IWindowImpl mainWindow)
            {
                return new ManagedWindowImpl(mainWindow);
            }
        }
    }
}
