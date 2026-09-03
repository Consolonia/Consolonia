using Avalonia.Input;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.ManagedWindows
{
    //todo: fix WindowsPanel -> this one is needed to override focus behavior of WindowsPanel
    public class WindowsPanelWorkaround : WindowsPanel
    {
        public WindowsPanelWorkaround()
        {
            // otherwise this panel itself is focused, for user looks like focus is nowhere
            Focusable = false;

            // GalleryWindows -> needed to tab out to list again
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
        }
    }
}