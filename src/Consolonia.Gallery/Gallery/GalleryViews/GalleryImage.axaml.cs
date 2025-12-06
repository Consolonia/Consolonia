using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryImage : UserControl
    {
        public GalleryImage()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            IStorageProvider storageProvider = TopLevel.GetTopLevel(this).StorageProvider;
            if (storageProvider.CanOpen)
            {
                IStorageFolder startLocation =
                    await storageProvider.TryGetFolderFromPathAsync(Environment.CurrentDirectory);
                IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open image",
                    AllowMultiple = false,
                    SuggestedStartLocation = startLocation,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("Image files") { Patterns = ["*.jpg", "*.jpeg", "*.png"] },
                        new("*.* files") { Patterns = ["*.*"] }
                    }
                });

                IStorageFile file = files?.FirstOrDefault();
                if (file != null)
                {
                    BigImage.Source = new Bitmap(file.Path.LocalPath);
                    BigImage.IsVisible = true;
                    WrapPanel.IsVisible = false;
                }
            }
        }
    }
}