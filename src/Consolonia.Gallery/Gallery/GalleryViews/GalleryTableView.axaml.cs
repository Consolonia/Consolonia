using Avalonia.Controls;

// ReSharper disable UnusedMember.Global

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryTableView : UserControl
    {
        public GalleryTableView()
        {
            InitializeComponent();

            TableView1.ItemsSource = Countries.All;
        }
    }
}
