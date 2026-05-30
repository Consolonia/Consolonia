using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryAnsiArt : UserControl
    {
        public GalleryAnsiArt()
        {
            InitializeComponent();
        }

        private void NextButton_OnClick(object sender, RoutedEventArgs e)
        {
            Carousel.Next();
        }
    }
}
