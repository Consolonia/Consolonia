using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Consolonia.Core.Controls;

namespace Consolonia.ManagedWindows.Controls
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

        public bool HasSelection => Enumerable.Any<IStorageFolder>(SelectedFolders);

        protected override bool FilterItem(IStorageItem item)
        {
            return item is IStorageFolder;
        }
    }
}