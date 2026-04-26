using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryImage : UserControl
    {
        private const string DefaultImageFolder = @"H:\Family.Photos\2007\2007-02-28";
        private readonly Carousel _imageCarousel;
        public AvaloniaList<string> ImagePaths { get; } = [];

        public GalleryImage()
        {
            InitializeComponent();
            _imageCarousel = this.Get<Carousel>("ImageCarousel");
            DataContext = this;
            LoadBundledImages();
            ShowSelectedImage();
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
                    string filePath = file.Path.LocalPath;
                    if (!ImagePaths.Contains(filePath))
                        ImagePaths.Add(filePath);

                    _imageCarousel.SelectedItem = filePath;
                    ShowSelectedImage();
                }
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            _imageCarousel.Previous();
            ShowSelectedImage();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _imageCarousel.Next();
            ShowSelectedImage();
        }

        private void LoadBundledImages()
        {
            if (LoadImagesFromFolder(DefaultImageFolder))
                return;

            for (int i = 0; i < 10; i++)
            {
                string imageUri = $"avares://Consolonia.Gallery/Resources/{i}.jpg";
                ImagePaths.Add(imageUri);
            }
        }

        private bool LoadImagesFromFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return false;

            string[] imagePaths = Directory.GetFiles(folderPath)
                .Where(path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string imagePath in imagePaths)
                ImagePaths.Add(imagePath);

            return ImagePaths.Count > 0;
        }

        private void ShowSelectedImage()
        {
            if (ImagePaths.Count == 0)
                return;

            if (_imageCarousel.SelectedItem is not string imagePath)
            {
                _imageCarousel.SelectedIndex = 0;
                imagePath = ImagePaths[0];
            }

            ImageTitle.Text = Path.GetFileName(imagePath);
        }
    }

    public class ImagePathToBitmapConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, Bitmap> BitmapCache =
            new(StringComparer.OrdinalIgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
                return null;

            return BitmapCache.GetOrAdd(imagePath, static path =>
            {
                if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = AssetLoader.Open(new Uri(path));
                    return new Bitmap(stream);
                }

                return new Bitmap(path);
            });
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}