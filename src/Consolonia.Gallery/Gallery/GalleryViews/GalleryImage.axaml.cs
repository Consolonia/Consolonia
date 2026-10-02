using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryImage : UserControl
    {
        private const string PicsumListUrl = "https://picsum.photos/v2/list?page=1&limit=25";

        /// <summary>
        ///     A terminal renders an image into a handful of cells, so the multi-megapixel
        ///     originals are pure download cost.
        /// </summary>
        private const int MaxImageEdge = 1024;

        private readonly Carousel _imageCarousel;

        public GalleryImage()
        {
            InitializeComponent();
            _imageCarousel = this.Get<Carousel>("ImageCarousel");
            DataContext = this;
            _imageCarousel.SelectionChanged += (_, _) => OnSelectedImageChanged();
            LoadImages();
        }

        public AvaloniaList<GalleryImageItem> Images { get; } = [];

        private async void LoadImages()
        {
            IReadOnlyList<GalleryImageItem> items = await FetchPicsumImagesAsync();
            if (items.Count == 0)
                items = GetBundledImages();

            Images.AddRange(items);

            if (_imageCarousel.SelectedIndex < 0)
                _imageCarousel.SelectedIndex = 0;

            OnSelectedImageChanged();
        }

        /// <summary>
        ///     Asks Picsum which photos exist and turns each one into a down-scaled image url.
        ///     Returns an empty list when the gallery is running offline.
        /// </summary>
        private static async Task<IReadOnlyList<GalleryImageItem>> FetchPicsumImagesAsync()
        {
            var items = new List<GalleryImageItem>();

#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                string json = await GalleryImageItem.Client.GetStringAsync(new Uri(PicsumListUrl));

                using var document = JsonDocument.Parse(json);
                foreach (JsonElement photo in document.RootElement.EnumerateArray())
                {
                    string id = photo.GetProperty("id").GetString();
                    string author = photo.GetProperty("author").GetString();
                    (int width, int height) = ScaleToMaxEdge(photo.GetProperty("width").GetInt32(),
                        photo.GetProperty("height").GetInt32());

                    items.Add(new GalleryImageItem(
                        $"https://picsum.photos/id/{id}/{width}/{height}",
                        $"{author} (#{id})"));
                }
            }
            catch (Exception)
            {
                return [];
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return items;
        }

        private static IReadOnlyList<GalleryImageItem> GetBundledImages()
        {
            return Enumerable.Range(0, 10)
                .Select(i => new GalleryImageItem($"avares://Consolonia.Gallery/Resources/{i}.jpg", $"{i}.jpg"))
                .ToArray();
        }

        /// <summary>
        ///     Shrinks <paramref name="width" /> x <paramref name="height" /> so the longest edge is at most
        ///     <see cref="MaxImageEdge" />, keeping the original aspect ratio.
        /// </summary>
        private static (int Width, int Height) ScaleToMaxEdge(int width, int height)
        {
            int longestEdge = Math.Max(width, height);
            if (longestEdge <= MaxImageEdge)
                return (width, height);

            double scale = (double)MaxImageEdge / longestEdge;
            return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            IStorageProvider storageProvider = TopLevel.GetTopLevel(this).StorageProvider;
            if (!storageProvider.CanOpen)
                return;

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
            if (file == null)
                return;

            string filePath = file.Path.LocalPath;
            GalleryImageItem item = Images.FirstOrDefault(i =>
                string.Equals(i.Source, filePath, StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                item = new GalleryImageItem(filePath, Path.GetFileName(filePath));
                Images.Add(item);
            }

            _imageCarousel.SelectedItem = item;
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            _imageCarousel.Previous();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _imageCarousel.Next();
        }

        /// <summary>
        ///     Updates the caption and pulls down the selected photo, plus its neighbours so that
        ///     paging through the carousel does not wait on the network.
        /// </summary>
        private void OnSelectedImageChanged()
        {
            if (Images.Count == 0)
                return;

            int selectedIndex = Math.Max(0, _imageCarousel.SelectedIndex);
            ImageTitle.Text = $"{selectedIndex + 1}/{Images.Count}  {Images[selectedIndex].Title}";

            foreach (int index in new[] { selectedIndex, selectedIndex + 1, selectedIndex - 1 })
                if (index >= 0 && index < Images.Count)
                    _ = Images[index].LoadAsync();
        }
    }

    /// <summary>
    ///     One carousel entry. The bitmap is fetched lazily because the sources are remote urls.
    /// </summary>
    public partial class GalleryImageItem : ObservableObject
    {
        internal static readonly HttpClient Client = new();

        private readonly SemaphoreSlim _loadGate = new(1, 1);

        [ObservableProperty] private Bitmap _bitmap;

        public GalleryImageItem(string source, string title)
        {
            Source = source;
            Title = title;
        }

        /// <summary>
        ///     An http url, an avares resource uri, or a local file path.
        /// </summary>
        public string Source { get; }

        public string Title { get; }

        public async Task LoadAsync()
        {
            if (Bitmap != null)
                return;

            await _loadGate.WaitAsync();
            try
            {
                if (Bitmap != null)
                    return;

#pragma warning disable CA1031 // Do not catch general exception types
                try
                {
                    Bitmap = await DecodeAsync(Source);
                }
                catch (Exception)
                {
                    // A photo that will not load just stays blank in the carousel.
                }
#pragma warning restore CA1031 // Do not catch general exception types
            }
            finally
            {
                _loadGate.Release();
            }
        }

        private static async Task<Bitmap> DecodeAsync(string source)
        {
            if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                byte[] bytes = await Client.GetByteArrayAsync(new Uri(source));
                return await Task.Run(() => new Bitmap(new MemoryStream(bytes)));
            }

            if (source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using Stream stream = AssetLoader.Open(new Uri(source));
                return new Bitmap(stream);
            }

            return await Task.Run(() => new Bitmap(source));
        }
    }
}
