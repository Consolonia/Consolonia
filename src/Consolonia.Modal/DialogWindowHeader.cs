using Avalonia;
using Avalonia.Controls.Primitives;

namespace Consolonia.Modal
{
    public class DialogWindowHeader : TemplatedControl
    {
        public static readonly StyledProperty<string> TitleProperty =
            DialogWindow.TitleProperty.AddOwner<DialogWindowHeader>();

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly StyledProperty<object> IconProperty =
            DialogWindow.IconProperty.AddOwner<DialogWindowHeader>();

        public object Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly StyledProperty<bool> IsCloseButtonVisibleProperty =
            DialogWindow.IsCloseButtonVisibleProperty.AddOwner<DialogWindowHeader>();

        public bool IsCloseButtonVisible
        {
            get => GetValue(IsCloseButtonVisibleProperty);
            set => SetValue(IsCloseButtonVisibleProperty, value);
        }
    }
}
