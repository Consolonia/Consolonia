using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Consolonia.Core.Controls
{
    internal partial class FolderPickerViewModel : PickerViewModelBase<FolderPickerOpenOptions>
    {
        [ObservableProperty] private ObservableCollection<IStorageFolder> _selectedFolders = new();

        [ObservableProperty] private SelectionMode _selectionMode;

        public FolderPickerViewModel(FolderPickerOpenOptions options)
            : base(options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            SelectionMode = options.AllowMultiple ? SelectionMode.Multiple : SelectionMode.Single;
            SelectedFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSelection));
        }

        public bool HasSelection => SelectedFolders.Any();

        protected override bool FilterItem(IStorageItem item)
        {
            return item is IStorageFolder;
        }
    }
}