using Avalonia.Controls;
using Avalonia.Interactivity;
using JetBrains.Annotations;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    [UsedImplicitly]
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
