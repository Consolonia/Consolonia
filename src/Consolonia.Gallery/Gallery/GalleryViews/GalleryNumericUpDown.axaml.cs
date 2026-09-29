using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryNumericUpDown : UserControl
    {
        public static readonly IValueConverter CultureConverter =
            new FuncValueConverter<CultureInfo, NumberFormatInfo>(c => (c ?? CultureInfo.CurrentCulture).NumberFormat);

        public GalleryNumericUpDown()
        {
            InitializeComponent();
            var viewModel = new NumbersPageViewModel();
            DataContext = viewModel;
        }
    }

    public class NumbersPageViewModel : ViewModelBase
    {
        private decimal _decimalValue;

        private double _doubleValue;
        private IList<FormatObject> _formats;
        private FormatObject _selectedFormat;
        private IList<Location> _spinnerLocations;

        public NumbersPageViewModel()
        {
            _selectedFormat = Formats.FirstOrDefault();
        }

        public double DoubleValue
        {
            get => _doubleValue;
            set => RaiseAndSetIfChanged(ref _doubleValue, value);
        }

        public decimal DecimalValue
        {
            get => _decimalValue;
            set => RaiseAndSetIfChanged(ref _decimalValue, value);
        }

        public IList<FormatObject> Formats =>
            _formats ?? (_formats = new List<FormatObject>
            {
                new() { Name = "Currency", Value = "C2" },
                new() { Name = "Fixed point", Value = "F2" },
                new() { Name = "General", Value = "G" },
                new() { Name = "Number", Value = "N" },
                new() { Name = "Percent", Value = "P" },
                new() { Name = "Degrees", Value = "{0:N2} °" }
            });

        public IList<Location> SpinnerLocations
        {
            get
            {
                if (_spinnerLocations == null)
                {
                    _spinnerLocations = new List<Location>();
                    foreach (Location value in Enum.GetValues(typeof(Location))) _spinnerLocations.Add(value);
                }

                return _spinnerLocations;
            }
        }


        // Trimmed-mode friendly where we might not have cultures
        public IList<CultureInfo> Cultures { get; } = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(c => new[] { "en-US", "en-GB", "fr-FR", "ar-DZ", "zh-CH", "cs-CZ" }.Contains(c.Name))
            .ToArray();

        public FormatObject SelectedFormat
        {
            get => _selectedFormat;
            set => RaiseAndSetIfChanged(ref _selectedFormat, value);
        }
    }

    public class FormatObject
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }
}