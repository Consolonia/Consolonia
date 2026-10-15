using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    // ReSharper disable once UnusedType.Global
    public partial class GalleryAnimations : UserControl
    {
        private const string BackgroundUrl = "https://picsum.photos/1024/768";
        private const string FallbackBackground = "avares://Consolonia.Gallery/Resources/0.jpg";

        /// <summary>How long to wait for Picsum before showing the bundled image instead.</summary>
        private static readonly TimeSpan BackgroundTimeout = TimeSpan.FromSeconds(5);

        public GalleryAnimations()
        {
            InitializeComponent();
            LoadBackground();
        }

        /// <summary>
        ///     Puts a random Picsum photo behind the animations, or a bundled one when offline.
        /// </summary>
        private async void LoadBackground()
        {
            BackgroundImage.Source = await DownloadBackgroundAsync() ?? LoadFallbackBackground();
        }

        /// <returns>The downloaded photo, or null when offline or when the reply is not an image.</returns>
        private static async Task<Bitmap> DownloadBackgroundAsync()
        {
            if (!GalleryHttp.UseNetwork)
                return null;

            byte[] bytes;
            try
            {
                using var timeout = new CancellationTokenSource(BackgroundTimeout);
                bytes = await GalleryHttp.Client.GetByteArrayAsync(new Uri(BackgroundUrl), timeout.Token);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return null;
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    return new Bitmap(stream);
                }
                catch (ArgumentException)
                {
                    // not an image (an error page, a truncated download): show the bundled image instead
                    return null;
                }
            });
        }

        private static Bitmap LoadFallbackBackground()
        {
            using Stream stream = AssetLoader.Open(new Uri(FallbackBackground));
            return new Bitmap(stream);
        }

        private async void PauseButton_OnClick(object _, RoutedEventArgs _2)
            // ReSharper restore UnusedParameter.Local
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);
            // ReSharper disable PossibleNullReferenceException //todo: build task does not understand ! operator
            await ((ConsoloniaLifetime)Application.Current!.ApplicationLifetime!).DisconnectFromConsoleAsync(cts.Token);
            // ReSharper restore PossibleNullReferenceException
        }


        // ReSharper disable UnusedParameter.Local
    }
}