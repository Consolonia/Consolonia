using Avalonia.Platform;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Factory for creating secondary window implementations (dialogs, etc.).
    /// </summary>
    public interface IChildWindowImplFactory
    {
        IWindowImpl CreateChildWindow(IWindowImpl mainWindow);
    }
}
