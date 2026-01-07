using System;
using System.Collections.Specialized;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.Core.Controls
{
    internal partial class FileSavePicker : ManagedWindow
    {
        public FileSavePicker()
            : this(new FilePickerSaveOptions())
        {
        }

        public FileSavePicker(FilePickerSaveOptions options)
        {
            DataContext = new FileSavePickerViewModel(options);
            InitializeComponent();

            CurrentFolderTextBox.Focus();
            ItemsListBox.Items.CollectionChanged += Items_CollectionChanged;
        }

        /// <summary>
        ///     Gets the file picker save options associated with this dialog.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when DataContext is null or of incorrect type.</exception>
        public FilePickerSaveOptions Options =>
            (DataContext as FileSavePickerViewModel)?.Options
            ?? throw new InvalidOperationException($"Invalid DataContext. Expected {nameof(FileSavePickerViewModel)}");

        /// <summary>
        ///     Gets the view model associated with this dialog.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when DataContext is null or of incorrect type.</exception>
        private FileSavePickerViewModel ViewModel =>
            DataContext as FileSavePickerViewModel
            ?? throw new InvalidOperationException($"Invalid DataContext. Expected {nameof(FileSavePickerViewModel)}");

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!CurrentFolderTextBox.IsFocused)
                Dispatcher.UIThread.Post(() => (ItemsListBox.ContainerFromIndex(0) as ListBoxItem)?.Focus(),
                    DispatcherPriority.Background);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            Position = new PixelPoint(2, 2);
            Width = WindowsPanel.Bounds.Width - 4;
            Height = WindowsPanel.Bounds.Height - 4;
        }


        private void OnDoubleTapped(object sender, TappedEventArgs e)
        {
            var listbox = (ListBox)sender;
            if (listbox.SelectedItem is IStorageFolder folder)
            {
                ViewModel.CurrentFolder = folder;
                ViewModel.CurrentFolderPath = folder.Path.LocalPath;
            }
            else if (listbox.SelectedItem is IStorageFile file)
            {
                ViewModel.SavePath = file.Name;
                OnOK(sender, e);
            }
        }

        private async void OnOK(object sender, RoutedEventArgs e)
        {
            ListBoxItem focusedListBoxItem = ItemsListBox.GetFocusedListBoxItem();
            if (focusedListBoxItem != null)
            {
                object item = ItemsListBox.ItemFromContainer(focusedListBoxItem);
                if (item is IStorageFolder folder)
                {
                    ViewModel.CurrentFolder = folder;
                    ViewModel.CurrentFolderPath = folder.Path.LocalPath;
                    // ReSharper disable once AssignNullToNotNullAttribute
                    ViewModel.SelectedItem = null;
                    e.Handled = true;
                    return;
                }
            }

            if (ViewModel.HasSelection)
            {
                // get an IStorageFile from the savepath
                IStorageProvider storageProvider = TopLevel.GetTopLevel(this).StorageProvider;
                ArgumentNullException.ThrowIfNull(storageProvider);

                string savePath = ViewModel.SavePath;
                if (!Path.IsPathFullyQualified(ViewModel.SavePath))
                    savePath = Path.GetFullPath(
                        Path.Combine(ViewModel.CurrentFolder.Path.LocalPath, ViewModel.SavePath));

                IStorageFile file =
                    await storageProvider.TryGetFileFromPathAsync(new Uri($"file://{savePath}"));
                if (file == null)
                {
                    IStorageFolder folder =
                        await storageProvider.TryGetFolderFromPathAsync(
                            new Uri($"file://{Path.GetDirectoryName(savePath)}"));
                    if (folder == null)
                    {
                        Close();
                        return;
                    }

                    file = await folder.CreateFileAsync(Path.GetFileName(savePath));
                }

                Close(file);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}