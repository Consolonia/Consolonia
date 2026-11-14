using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    // ReSharper disable once UnusedType.Global
    public partial class GalleryAnimations : UserControl
    {
        public GalleryAnimations()
        {
            InitializeComponent();
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