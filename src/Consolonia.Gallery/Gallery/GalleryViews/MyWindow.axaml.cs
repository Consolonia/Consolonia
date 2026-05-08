#pragma warning disable CA5394 // Do not use insecure randomness
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class MyWindow : Window
    {
        private static int _windowCount;

        private static readonly IImmutableSolidColorBrush[] SomeBrushes =
        [
            Brushes.LightBlue,
            Brushes.LightGreen,
            Brushes.LightCyan,
            Brushes.LightSalmon,
            Brushes.LightSeaGreen,
            Brushes.LightSlateGray,
            Brushes.LightCoral,
            Brushes.LightGoldenrodYellow,
            Brushes.LightPink
        ];

        public MyWindow()
        {
            this.Opened += MyWindow_Opened;
            InitializeComponent();

            //  this.Background = brushes[Random.Shared.Next(0, brushes.Length)];

            DataContext = new MyWindowViewModel
            {
                Counter = 0,
                Title = $"New Window {++_windowCount}"
            };

            AttachedToVisualTree += OnAttachedToVisualTree;
        }

        private async void MyWindow_Opened(object sender, EventArgs e)
        {
            //await Task.Yield(); // ensures layout pass completed
            this.IncrementButton.Focus();
        }

        public MyWindowViewModel ViewModel => (MyWindowViewModel)DataContext;

        private void OnAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            if ((this.FindResource("ThemeName") as string)?.StartsWith("TurboVision",
                    StringComparison.OrdinalIgnoreCase) == true)
                TurboVisionShortCutsTextBlock.IsVisible = true;
        }

        private void OnIncrement(object sender, RoutedEventArgs args)
        {
            var vm = (MyWindowViewModel)DataContext;
            if (vm != null) vm.Counter++;
        }

        private void OnColor(object sender, RoutedEventArgs args)
        {
            Background = SomeBrushes[Random.Shared.Next(0, SomeBrushes.Length)];
        }

        private void OnClose(object sender, RoutedEventArgs args)
        {
            Close();
        }


        private async void OnShowDialog(object sender, RoutedEventArgs args)
        {
            var dialog = new MyDialog
            {
                WindowStartupLocation =
                    Enum.Parse<WindowStartupLocation>(
                        (StartupLocationCombo.SelectedItem as ComboBoxItem).Tag.ToString()),
                SizeToContent =
                    Enum.Parse<SizeToContent>(((ComboBoxItem)SizeToContentCombo.SelectedItem).Tag.ToString())
            };
            dialog.ViewModel.Text = ViewModel.Text;

            dialog.SizeToBounds(Bounds);

            string result = await dialog.ShowDialog<string>(this);
            if (result != null)
                ViewModel.Text = result;
        }
    }

    public partial class MyWindowViewModel : ObservableObject
    {
        [ObservableProperty] private int _counter;

        [ObservableProperty] private string _text = string.Empty;

        [ObservableProperty] private string _title = "New Window";
    }

    internal static class Utils
    {
        public static void SizeToBounds(this Window window, Rect rect)
        {
            int minWidth = (int)rect.Width / 4;
            int minHeight = (int)rect.Height / 4;
            int maxWidth = (int)rect.Width / 2;
            int maxHeight = (int)rect.Height / 2;

            if (window.WindowStartupLocation == WindowStartupLocation.Manual)
                window.Position = new PixelPoint(Random.Shared.Next(0, (int)rect.Width - maxWidth),
                    Random.Shared.Next(0, (int)rect.Height - maxHeight));

            switch (window.SizeToContent)
            {
                case SizeToContent.Manual:
                    // we are providing both width and height
                    window.Width = Random.Shared.Next(minWidth, maxWidth);
                    window.Height = Random.Shared.Next(minHeight, maxHeight);
                    break;
                case SizeToContent.Width:
                    // we are expecting window to figure out width
                    window.Height = Random.Shared.Next(minHeight, maxHeight);
                    break;
                case SizeToContent.Height:
                    // we are expecting window to figure out height
                    window.Width = Random.Shared.Next(minWidth, maxWidth);
                    break;
                case SizeToContent.WidthAndHeight:
                    // we are expecting window to figure out both width and height
                    break;
            }
        }
    }
}