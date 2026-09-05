#pragma warning disable CA5394 // Do not use insecure randomness
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class SomeDialogWindow : Window
    {
        internal const string DialogTitle = "Dialog popup";

        private static readonly Random Random = new();

        public SomeDialogWindow(double width, double height)
        {
            InitializeComponent();

            Title = DialogTitle;
            Width = width;
            Height = height;
            AttachedToVisualTree += OnShowDialog;
        }

        // ReSharper disable once MemberCanBePrivate.Global Can be used by constructor
        public SomeDialogWindow() : this(26 + Random.Next(20), 20 + Random.Next(10))
        {
        }

        private void OnShowDialog(object sender, VisualTreeAttachmentEventArgs e)
        {
            AttachedToVisualTree -= OnShowDialog;
            OneMoreButton.AttachedToVisualTree += OnButtonAttached;
        }

        private void OnButtonAttached(object sender, VisualTreeAttachmentEventArgs e)
        {
            OneMoreButton.AttachedToVisualTree -= OnButtonAttached;
            OneMoreButton.Focus();
        }

        private async void OneMore_Clicked(object sender, RoutedEventArgs e)
        {
            await new SomeDialogWindow().ShowDialog(this);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}