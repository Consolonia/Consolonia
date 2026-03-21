using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using DotNet.Globbing;

namespace Consolonia.ManagedWindows.Controls
{
    internal partial class FileOpenPickerViewModel : PickerViewModelBase<FilePickerOpenOptions>
    {
        [ObservableProperty] private ObservableCollection<IStorageFile> _selectedFiles = new();

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurrentFolderPath))]
        private int _selectedFilterIndex;

        [ObservableProperty] private SelectionMode _selectionMode;

        public FileOpenPickerViewModel(FilePickerOpenOptions options)
            : base(options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            SelectionMode = options.AllowMultiple ? SelectionMode.Multiple : SelectionMode.Single;
            SelectedFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSelection));
        }

        public bool HasSelection => SelectedFiles.Any();

        protected override bool FilterItem(IStorageItem item)
        {
            if (Options.FileTypeFilter == null || Options.FileTypeFilter.Count == 0)
                return true;

            if (item is IStorageFile file)
            {
                FilePickerFileType selectedFileType = Options.FileTypeFilter[SelectedFilterIndex]!;
                if (selectedFileType.Patterns == null)
                    return true;

                foreach (string pattern in selectedFileType.Patterns)
                    if (Glob.Parse(pattern, new GlobOptions
                        {
                            Evaluation = new EvaluationOptions
                            {
                                CaseInsensitive = true
                            }
                        }).IsMatch(file.Name))
                        return true;
                return false;
            }

            // show folders
            return true;
        }
    }
}