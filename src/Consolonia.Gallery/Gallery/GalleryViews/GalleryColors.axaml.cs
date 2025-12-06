using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "This is not a security context")]
    public partial class GalleryColors : UserControl
    {
        public GalleryColors()
        {
            InitializeComponent();
            Random rnd = Random.Shared;
            DataContext = new RgbModel
                { Color = Color.FromRgb((byte)rnd.Next(255), (byte)rnd.Next(255), (byte)rnd.Next(255)) };

            FourBitControl.ItemsSource = ConsoleColorItem.GetAll();
        }

        private void Random_Click(object sender, RoutedEventArgs e)
        {
            var rnd = new Random();
            var model = (RgbModel)DataContext;
            model.Color = Color.FromRgb((byte)rnd.Next(255), (byte)rnd.Next(255), (byte)rnd.Next(255));
        }

        private void Red_Click(object sender, RoutedEventArgs e)
        {
            var rnd = new Random();
            var model = (RgbModel)DataContext;
            model.Color = Color.FromRgb((byte)rnd.Next(255), model.Color.G, model.Color.B);
        }

        private void Green_Click(object sender, RoutedEventArgs e)
        {
            var rnd = new Random();
            var model = (RgbModel)DataContext;
            model.Color = Color.FromRgb(model.Color.R, (byte)rnd.Next(255), model.Color.B);
        }

        private void Blue_Click(object sender, RoutedEventArgs e)
        {
            var rnd = new Random();
            var model = (RgbModel)DataContext;
            model.Color = Color.FromRgb(model.Color.R, model.Color.G, (byte)rnd.Next(255));
        }
    }

    public class RgbModel : INotifyPropertyChanged
    {
        private IBrush _brush;

        private Color _color;

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                Brush = new SolidColorBrush(_color);
            }
        }

        public IBrush Brush
        {
            get => _brush;
            set
            {
                _brush = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
            }
        }

        public ObservableCollection<Swatch> Swatches { get; } = new();

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class Swatch
    {
        public string Name { get; set; }

        public Color Color { get; set; }
    }

    public class ConsoleColorItem
    {
        public Brush Brush { get; set; }

        public string Name { get; set; }

        public static ConsoleColorItem[] GetAll()
        {
            return Enum.GetValues<ConsoleColor>()
                .Select(c => new ConsoleColorItem
                {
                    Brush = new SolidColorBrush(new EgaConsoleColorMode(true).ConvertToAvaloniaColor(c, true)),
                    Name = c.ToString()
                })
                .ToArray();
        }
    }
}