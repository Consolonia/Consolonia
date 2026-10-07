using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

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

        /// <summary>
        ///     How long to wait for Picsum's photo list before showing the bundled images instead.
        /// </summary>
        private static readonly TimeSpan PicsumListTimeout = TimeSpan.FromSeconds(5);

        private static readonly HttpClient Client = new();

        public GalleryImage()
        {
            InitializeComponent();
            LoadThumbnails();
        }

        private async void LoadThumbnails()
        {
            IReadOnlyList<string> sources = await FetchPicsumSourcesAsync();
            if (sources.Count == 0)
                sources = Enumerable.Range(0, 10)
                    .Select(i => $"avares://Consolonia.Gallery/Resources/{i}.jpg")
                    .ToArray();

            foreach (string source in sources)
            {
                var image = new Image { Stretch = Stretch.Uniform };
                var border = new Border { Child = image };
                border.Classes.Add("thumbnail");
                WrapPanel.Children.Add(border);
                _ = LoadBitmapAsync(image, source);
            }
        }

        /// <summary>
        ///     Asks Picsum which photos exist and turns each one into a down-scaled image url.
        ///     Returns an empty list when the gallery is running offline.
        /// </summary>
        private static async Task<IReadOnlyList<string>> FetchPicsumSourcesAsync()
        {
            var sources = new List<string>();

            try
            {
                using var timeout = new CancellationTokenSource(PicsumListTimeout);
                string json = await Client.GetStringAsync(new Uri(PicsumListUrl), timeout.Token);

                using JsonDocument document = JsonDocument.Parse(json);
                foreach (JsonElement photo in document.RootElement.EnumerateArray())
                {
                    string id = photo.GetProperty("id").GetString();
                    (int width, int height) = ScaleToMaxEdge(photo.GetProperty("width").GetInt32(),
                        photo.GetProperty("height").GetInt32());

                    sources.Add($"https://picsum.photos/id/{id}/{width}/{height}");
                }
            }
            // InvalidOperationException, KeyNotFoundException and FormatException: a reply that is
            // JSON but not the list of photos expected.
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException
                                          or InvalidOperationException or KeyNotFoundException
                                          or FormatException)
            {
                return [];
            }

            return sources;
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

        private static async Task LoadBitmapAsync(Image image, string source)
        {
            try
            {
                if (source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    using Stream stream = AssetLoader.Open(new Uri(source));
                    image.Source = new Bitmap(stream);
                    return;
                }

                byte[] bytes = await Client.GetByteArrayAsync(new Uri(source));
                image.Source = await Task.Run(() =>
                {
                    using var stream = new MemoryStream(bytes);
                    return new Bitmap(stream);
                });
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // A photo that will not load just stays blank.
            }
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