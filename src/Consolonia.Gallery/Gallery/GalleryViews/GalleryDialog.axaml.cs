using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryDialog : UserControl
    {
        public GalleryDialog()
        {
            InitializeComponent();
        }

        private async void Button_OnClick(object _, RoutedEventArgs e)
        {
            var dialog = new SomeDialogWindow(50, 35);
            await dialog.ShowDialog(this.FindAncestorOfType<Window>());
        }
    }
}