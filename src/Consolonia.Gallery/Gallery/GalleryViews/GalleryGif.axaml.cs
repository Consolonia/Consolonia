using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryGif : UserControl
    {
        private List<GifFrame> _frames;
        private int _currentFrame;
        private DispatcherTimer _timer;

        public GalleryGif()
        {
            InitializeComponent();
            LoadGif("avares://Consolonia.Gallery/Resources/sample.gif");
        }

        private void LoadGif(string uri)
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            _frames = DecodeGifFrames(ms.ToArray());
            if (_frames.Count == 0)
                return;

            _currentFrame = 0;
            GifImage.Source = _frames[0].Bitmap;

            if (_frames.Count > 1)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_frames[0].DurationMs) };
                _timer.Tick += OnTimerTick;
                _timer.Start();
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _currentFrame = (_currentFrame + 1) % _frames.Count;
            GifImage.Source = _frames[_currentFrame].Bitmap;

            // Adjust interval for next frame's duration
            int nextFrame = (_currentFrame + 1) % _frames.Count;
            int duration = _frames[nextFrame].DurationMs;
            _timer.Interval = TimeSpan.FromMilliseconds(duration > 0 ? duration : 100);
        }

        private static List<GifFrame> DecodeGifFrames(byte[] data)
        {
            var frames = new List<GifFrame>();
            using var codec = SKCodec.Create(new MemoryStream(data));
            if (codec == null)
                return frames;

            var info = codec.Info;
            int frameCount = codec.FrameCount;

            // Compositing surface - GIF frames may be partial and depend on previous frames
            using var compositeBitmap = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(compositeBitmap);
            canvas.Clear(SKColors.Transparent);

            for (int i = 0; i < frameCount; i++)
            {
                var frameInfo = codec.FrameInfo[i];
                int duration = frameInfo.Duration;
                if (duration <= 0) duration = 100;

                // Decode this frame
                var opts = new SKCodecOptions(i);
                using var frameBitmap = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                codec.GetPixels(frameBitmap.Info, frameBitmap.GetPixels(), opts);

                // Composite onto the canvas
                canvas.DrawBitmap(frameBitmap, 0, 0);

                // Snapshot the composited result as an Avalonia bitmap
                using var image = SKImage.FromBitmap(compositeBitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var bitmapStream = new MemoryStream(encoded.ToArray());
                var avaloniaBitmap = new Bitmap(bitmapStream);

                frames.Add(new GifFrame(avaloniaBitmap, duration));

                // Handle disposal method
                if (frameInfo.DisposalMethod == SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
                {
                    canvas.Clear(SKColors.Transparent);
                }
            }

            return frames;
        }

        private record GifFrame(Bitmap Bitmap, int DurationMs);
    }
}
