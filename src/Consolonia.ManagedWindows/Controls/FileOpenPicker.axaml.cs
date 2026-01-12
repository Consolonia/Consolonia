using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Consolonia.Core.Controls;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.ManagedWindows.Controls
{
    internal partial class FileOpenPicker : ManagedWindow
    {
        public FileOpenPicker()
            : this(new FilePickerOpenOptions())
        {
        }

        public FileOpenPicker(FilePickerOpenOptions options)
        {
            DataContext = new FileOpenPickerViewModel(options);
            InitializeComponent();

            CurrentFolderTextBox.Focus();
            ItemsListBox.Items.CollectionChanged += Items_CollectionChanged;
        }

        /// <summary>
        ///     Gets the view model associated with this picker.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the DataContext is null or not of type FileOpenPickerViewModel.</exception>
        private FileOpenPickerViewModel ViewModel =>
            DataContext as FileOpenPickerViewModel
            ?? throw new InvalidOperationException("DataContext is not properly initialized.");

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
            if (ItemsListBox.SelectedItem is IStorageFolder folder)
            {
                ViewModel.CurrentFolder = folder;
                ViewModel.CurrentFolderPath = folder.Path.LocalPath;
                ViewModel.SelectedFiles.Clear();
            }
            else if (ItemsListBox.SelectedItem is IStorageFile file)
            {
                Close(new[] { file });
            }

            e.Handled = true;
        }

        private void OnOK(object sender, RoutedEventArgs e)
        {
            ListBoxItem focusedListBoxItem = ListBoxExtensions.GetFocusedListBoxItem(ItemsListBox);
            if (focusedListBoxItem != null)
            {
                object item = ItemsListBox.ItemFromContainer(focusedListBoxItem);
                if (item is IStorageFolder folder)
                {
                    ViewModel.CurrentFolder = folder;
                    ViewModel.CurrentFolderPath = folder.Path.LocalPath;
                    ViewModel.SelectedFiles.Clear();
                    e.Handled = true;
                    return;
                }
            }

            if (ViewModel.HasSelection) Close(ViewModel.SelectedFiles);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel.SelectionMode == SelectionMode.Single)
            {
                if (e.AddedItems.Count > 0 &&
                    e.AddedItems[0] is IStorageFile file)
                {
                    ViewModel.SelectedFiles.Clear();
                    ViewModel.SelectedFiles.Add(file);
                }
            }
            else
            {
                foreach (object item in e.AddedItems)
                    if (item is IStorageFile file)
                        ViewModel.SelectedFiles.Add(file);

                foreach (object item in e.RemovedItems)
                    if (item is IStorageFile file)
                        ViewModel.SelectedFiles.Remove(file);
            }
        }
    }
}