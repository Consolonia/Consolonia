using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Consolonia.Core.Controls
{
    internal abstract partial class PickerViewModelBase<TPickerOptions> : ObservableObject
        where TPickerOptions : PickerOptions
    {
        [ObservableProperty] private IStorageFolder _currentFolder;

        [ObservableProperty] private string _currentFolderPath;

        [ObservableProperty] private ObservableCollection<IStorageItem> _items = new();


        [ObservableProperty] private TPickerOptions _options;

        protected PickerViewModelBase(TPickerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            Options = options;
            CurrentFolderPath = options.SuggestedStartLocation?.Path.LocalPath ?? Environment.CurrentDirectory;
            CurrentFolder = options.SuggestedStartLocation ?? new SystemStorageFolder(Environment.CurrentDirectory);
            _ = LoadCurrentFolder();
            PropertyChanged += PickerViewModel_PropertyChanged;
        }


        private async void PickerViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CurrentFolderPath):
                    try
                    {
                        if (!string.IsNullOrEmpty(CurrentFolderPath))
                            CurrentFolder = new SystemStorageFolder(new DirectoryInfo(Path.Combine(CurrentFolderPath)));
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // ignore
                    }

                    break;

                case nameof(CurrentFolder):
                    await LoadCurrentFolder();
                    break;
            }
        }

        private async Task LoadCurrentFolder()
        {
            Items.Clear();

            Items.Add(
                new SystemStorageFolder(new DirectoryInfo(Path.Combine(CurrentFolder.Path.LocalPath, "..")), true));

            await foreach (IStorageItem item in CurrentFolder.GetItemsAsync())
                if (FilterItem(item))
                    Items.Add(item);
        }

        protected abstract bool FilterItem(IStorageItem item);
    }
}