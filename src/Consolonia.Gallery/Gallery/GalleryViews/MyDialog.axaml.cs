#pragma warning disable CA5394 // Do not use insecure randomness
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class MyDialog : Window
    {
        private static int _dialogCount;

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

        public MyDialog()
        {
            InitializeComponent();
            // this.Background = brushes[Random.Shared.Next(0, brushes.Length)];

            DataContext = new MyDialogViewModel
            {
                Title = $"New Dialog {++_dialogCount}"
            };
        }

        public MyDialogViewModel ViewModel => (MyDialogViewModel)DataContext;

        private void OnOK(object sender, RoutedEventArgs args)
        {
            Close(ViewModel.Text);
        }

        private void OnCancel(object sender, RoutedEventArgs args)
        {
            Close(null);
        }

        private void OnColor(object sender, RoutedEventArgs args)
        {
            Background = SomeBrushes[Random.Shared.Next(0, SomeBrushes.Length)];
        }
    }

    public partial class MyDialogViewModel : ObservableObject
    {
        [ObservableProperty] private string _text = "";

        [ObservableProperty] private string _title = "New Dialog";
    }
}