using System;
using System.ComponentModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Consolonia.ManagedWindows.Controls
{
    internal partial class FileSavePickerViewModel : PickerViewModelBase<FilePickerSaveOptions>
    {
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))]
        private string _savePath = string.Empty;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedFile))]
        private IStorageItem _selectedItem;

        public FileSavePickerViewModel(FilePickerSaveOptions options)
            : base(options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            PropertyChanged += FileSavePickerViewModel_PropertyChanged;
        }

        public bool HasSelection => !string.IsNullOrEmpty(SavePath);

        public IStorageFile SelectedFile => SelectedItem as IStorageFile;

        private void FileSavePickerViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedFile)) SavePath = SelectedFile?.Name ?? string.Empty;
        }

        protected override bool FilterItem(IStorageItem item)
        {
            return true;
        }
    }
}