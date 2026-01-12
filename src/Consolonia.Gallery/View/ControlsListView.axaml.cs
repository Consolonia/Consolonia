using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Consolonia.Gallery.Gallery;
using Consolonia.Themes;

namespace Consolonia.Gallery.View
{
    public enum ThemesList
    {
        Modern,
        ModernContrast,
        TurboVision,
        TurboVisionCompatible,
        TurboVisionGray,
        TurboVisionElegant
    }

    public partial class ControlsListView : UserControl
    {
        private static readonly HttpClient Client = new();
        private readonly IEnumerable<GalleryItem> _items;
        private string[] _commandLineArgs;

        public ControlsListView()
        {
            InitializeComponent();

            DataContext = new ControlsListViewModel();

            ViewModel.SelectedTheme = Application.Current.Styles[0].GetType().ToString().Split('.').Last()
                .Replace("Theme", string.Empty, StringComparison.OrdinalIgnoreCase);

            GalleryGrid.ItemsSource = _items = GalleryItem.Enumerated.ToArray();

            if (Application.Current!.ApplicationLifetime is ConsoloniaLifetime lifetime)
                _commandLineArgs = lifetime!.Args!;
            else
                _commandLineArgs = [];

            TrySetupSelected();

            Loaded += OnLoaded;
        }

        public ControlsListViewModel ViewModel => (ControlsListViewModel)DataContext!;

        private void TrySetupSelected()
        {
            string[] commandLineArgs = _commandLineArgs.Where(s => s != null)
                .Where(s => !s.EndsWith(App.TurboVisionProgramParameter, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (commandLineArgs.Length == 0)
            {
                GalleryGrid.SelectedIndex = 0;
                return;
            }

            string itemToSelectName = commandLineArgs.Last();
            GalleryItem itemToSelect;
            try
            {
                itemToSelect = _items.SingleOrDefault(item =>
                    string.Equals(item.Name, itemToSelectName, StringComparison.OrdinalIgnoreCase));
                if (itemToSelect == null)
                    throw new ArgumentOutOfRangeException(
                        $"No item with name {itemToSelectName} found. List of possible item names: {string.Join(", ", GalleryItem.Enumerated.Select(item => item.Name))}");
            }
            catch (InvalidOperationException)
            {
                throw new InvalidProgramException(
                    $"Several gallery items found with provided name {itemToSelectName}");
            }

            GalleryGrid.SelectedItem = itemToSelect;

            GalleryGrid.Focus(); // F393122D-9623-4535-A87A-F031C2769386
        }


        public void ChangeTo(string[] args)
        {
            _commandLineArgs = args;
            TrySetupSelected();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            var lifetime = Application.Current!.ApplicationLifetime as ConsoloniaLifetime;
            lifetime.TryShutdown();
        }

        private async void OnShowXaml(object sender, RoutedEventArgs e)
        {
            var selectedItem = GalleryGrid.SelectedItem as GalleryItem;
            string xamlFile = $"{selectedItem.Type.Name}.axaml";
            await ShowCode(xamlFile);

            await RefocusList();
        }

        private async Task RefocusList()
        {
            // F393122D-9623-4535-A87A-F031C2769386
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (GalleryGrid.SelectedIndex < 0 && GalleryGrid.ItemCount > 0)
                    GalleryGrid.SelectedIndex = 0;

                if (GalleryGrid.SelectedIndex >= 0 && GalleryGrid.ContainerFromIndex(GalleryGrid.SelectedIndex) is
                        { } c) c.Focus();
            }, DispatcherPriority.Input);
        }

        private async void OnShowCodeBehind(object sender, RoutedEventArgs e)
        {
            var selectedItem = GalleryGrid.SelectedItem as GalleryItem;
            string xamlFile = $"{selectedItem.Type.Name}.axaml.cs";
            await ShowCode(xamlFile);

            await RefocusList();
        }

        private static async Task ShowCode(string xamlFile)
        {
            string xaml = await Client.GetStringAsync(new Uri(
                $"https://raw.githubusercontent.com/jinek/Consolonia/refs/heads/main/src/Consolonia.Gallery/Gallery/GalleryViews/{xamlFile}"));

            var dialog = new XamlDialogWindow
            {
                Title = xamlFile,
                // ReSharper disable once MethodHasAsyncOverload
                DataContext = xaml
            };
            await dialog.ShowDialog();
        }

        private void OnThemeVariantLightMenuClick(object sender, RoutedEventArgs e)
        {
            ViewModel.RequestedThemeVariant = ThemeVariant.Light;
        }

        private void OnThemeVariantDarkMenuClick(object sender, RoutedEventArgs e)
        {
            ViewModel.RequestedThemeVariant = ThemeVariant.Dark;
        }

        private void OnThemeMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string themeName } ||
                !Enum.TryParse(themeName, out ThemesList selectedTheme))
                return;
            
            Window mainWindow = ((IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).MainWindow;
            mainWindow.Content = null; // otherwise Avalonia sets some trash template to WindowsPanel

            // NOTE: this assumes first style object is the old theme!
            Application.Current.Styles[0] = selectedTheme switch
            {
                ThemesList.Modern => new ModernTheme(),
                ThemesList.ModernContrast => new ModernContrastTheme(),
                ThemesList.TurboVision => new TurboVisionTheme(),
                ThemesList.TurboVisionCompatible => new TurboVisionCompatibleTheme(),
                ThemesList.TurboVisionGray => new TurboVisionGrayTheme(),
                ThemesList.TurboVisionElegant => new TurboVisionElegantTheme(),
                _ => throw new InvalidDataException("Unknown theme name")
            };

            ViewModel.SelectedTheme = themeName;

            mainWindow.Content = new ControlsListView { DataContext = DataContext };
        }

        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
        }
    }

    public partial class ControlsListViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModern))]
        [NotifyPropertyChangedFor(nameof(IsModernContrast))]
        [NotifyPropertyChangedFor(nameof(IsTurboVision))]
        [NotifyPropertyChangedFor(nameof(IsTurboVisionCompatible))]
        [NotifyPropertyChangedFor(nameof(IsTurboVisionGray))]
        [NotifyPropertyChangedFor(nameof(IsTurboVisionElegant))]
        private string _selectedTheme;

        public ThemeVariant RequestedThemeVariant
        {
            get => Application.Current!.RequestedThemeVariant;
            set
            {
                Application.Current!.RequestedThemeVariant = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLight));
                OnPropertyChanged(nameof(IsDark));
            }
        }

        public bool IsLight =>
            RequestedThemeVariant == ThemeVariant.Default || RequestedThemeVariant == ThemeVariant.Light;

        public bool IsDark => RequestedThemeVariant == ThemeVariant.Dark;

        public bool IsModern => SelectedTheme == nameof(ThemesList.Modern);
        public bool IsModernContrast => SelectedTheme == nameof(ThemesList.ModernContrast);
        public bool IsTurboVision => SelectedTheme == nameof(ThemesList.TurboVision);
        public bool IsTurboVisionCompatible => SelectedTheme == nameof(ThemesList.TurboVisionCompatible);
        public bool IsTurboVisionGray => SelectedTheme == nameof(ThemesList.TurboVisionGray);
        public bool IsTurboVisionElegant => SelectedTheme == nameof(ThemesList.TurboVisionElegant);
    }
}