using Avalonia.Controls;

namespace Consolonia.ManagedWindows.Controls
{
    internal static class ListBoxExtensions
    {
        internal static ListBoxItem GetFocusedListBoxItem(this ListBox listBox)
        {
            // 1) Check realized containers (fast, reliable when items are materialized)
            for (int i = 0; i < listBox.ItemCount; i++)
                if (listBox.ContainerFromIndex(i) is ListBoxItem container)
                    if (container.IsFocused || container.IsKeyboardFocusWithin)
                        return container;

            return null;
        }
    }
}