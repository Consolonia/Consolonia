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
using Avalonia.Layout;
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
        ///     The largest width or height, in pixels, to request from Picsum. A terminal shows an image in
        ///     only a few cells, so downloading the full multi-megapixel original would be wasted.
        /// </summary>
        private const int MaxImageEdge = 1024;

        /// <summary>
        ///     How long to wait for Picsum's photo list before showing the bundled images instead.
        /// </summary>
        private static readonly TimeSpan PicsumListTimeout = TimeSpan.FromSeconds(5);

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
                var border = new Border { Child = new Panel { Children = { image, CreateCaption() } } };
                border.Classes.Add("thumbnail");
                WrapPanel.Children.Add(border);
                _ = LoadBitmapAsync(image, source);
            }
        }

        /// <summary>
        ///     Creates two lines of text to show on top of a thumbnail. One line has an opaque background and
        ///     the other has a transparent background. This shows both kinds of text over an image.
        /// </summary>
        private static StackPanel CreateCaption()
        {
            return new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                Children =
                {
                    new TextBlock
                    {
                        Text = "White on dark blue",
                        Foreground = Brushes.White,
                        Background = Brushes.DarkBlue
                    },
                    new TextBlock
                    {
                        Text = "White on transparent",
                        Foreground = Brushes.White,
                        Background = Brushes.Transparent
                    }
                }
            };
        }

        /// <summary>
        ///     Gets the photo list from Picsum and returns a URL for a scaled-down copy of each photo.
        ///     Returns an empty list when offline or when the request fails.
        /// </summary>
        private static async Task<IReadOnlyList<string>> FetchPicsumSourcesAsync()
        {
            if (!GalleryHttp.UseNetwork)
                return [];

            var sources = new List<string>();

            try
            {
                using var timeout = new CancellationTokenSource(PicsumListTimeout);
                string json = await GalleryHttp.Client.GetStringAsync(new Uri(PicsumListUrl), timeout.Token);

                using JsonDocument document = JsonDocument.Parse(json);
                foreach (JsonElement photo in document.RootElement.EnumerateArray())
                {
                    string id = photo.GetProperty("id").GetString();
                    (int width, int height) = ScaleToMaxEdge(photo.GetProperty("width").GetInt32(),
                        photo.GetProperty("height").GetInt32());

                    sources.Add($"https://picsum.photos/id/{id}/{width}/{height}");
                }
            }
            // InvalidOperationException, KeyNotFoundException and FormatException mean the reply is
            // valid JSON but not the expected list of photos.
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

                byte[] bytes = await GalleryHttp.Client.GetByteArrayAsync(new Uri(source));
                image.Source = await Task.Run(() =>
                {
                    using var stream = new MemoryStream(bytes);
                    return new Bitmap(stream);
                });
            }
            // ArgumentException means the reply is not an image (for example an error page or a partial download).
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException)
            {
                // If a photo fails to download or decode, its thumbnail stays blank.
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