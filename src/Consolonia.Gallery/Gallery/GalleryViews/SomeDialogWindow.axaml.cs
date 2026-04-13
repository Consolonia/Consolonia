using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Consolonia.Modal;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class SomeDialogWindow : DialogWindow
    {
        private static readonly Random Random = new();

        public SomeDialogWindow(double width, double height)
        {
            InitializeComponent();
            /*Width = width;
            Height = height;*/
            Loaded += (_, _) =>
            {
                this.FindControl<Button>("Button")!.Focus();
            };
        }

        // ReSharper disable once MemberCanBePrivate.Global Can be used by constructor
        public SomeDialogWindow() : this(10 + Random.Next(20), 10 + Random.Next(10))
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ReSharper disable UnusedParameter.Local
        private async void OneMore_Clicked(object sender, RoutedEventArgs e)
            // ReSharper restore UnusedParameter.Local
        {
            await new SomeDialogWindow().ShowDialogAsync(this);
        }
    }
}