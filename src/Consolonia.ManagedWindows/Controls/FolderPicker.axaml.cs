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
    internal partial class FolderPicker : ManagedWindow
    {
        public FolderPicker()
            : this(new FolderPickerOpenOptions())
        {
        }

        public FolderPicker(FolderPickerOpenOptions options)
        {
            DataContext = new FolderPickerViewModel(options);
            InitializeComponent();

            CurrentFolderTextBox.Focus();
            ItemsListBox.Items.CollectionChanged += Items_CollectionChanged;
        }

        private FolderPickerViewModel ViewModel => (FolderPickerViewModel)DataContext;

        public FolderPickerOpenOptions Options =>
            ((FolderPickerViewModel)DataContext)?.Options ?? new FolderPickerOpenOptions();

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
            if (ItemsListBox.SelectedItem is SystemStorageFolder folder)
            {
                ViewModel.CurrentFolder = folder;
                ViewModel.CurrentFolderPath = folder.Path.LocalPath;
                ViewModel.SelectedFolders.Clear();
                e.Handled = true;
            }
        }

        private void OnOK(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            ListBoxItem focusedListBoxItem = ListBoxExtensions.GetFocusedListBoxItem(ItemsListBox);
            if (focusedListBoxItem != null)
            {
                object item = ItemsListBox.ItemFromContainer(focusedListBoxItem);
                if (item is IStorageFolder folder)
                {
                    ViewModel.CurrentFolder = folder;
                    ViewModel.CurrentFolderPath = folder.Path.LocalPath;
                    ViewModel.SelectedFolders.Clear();
                    return;
                }
            }

            if (ViewModel.SelectionMode == SelectionMode.Single &&
                !ViewModel.HasSelection)
                ViewModel.SelectedFolders.Add(ViewModel.CurrentFolder);


            if (ViewModel.HasSelection) Close(ViewModel.SelectedFolders);
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
                    e.AddedItems[0] is IStorageFolder folder)
                {
                    ViewModel.SelectedFolders.Clear();
                    if (folder.Name != "..")
                        ViewModel.SelectedFolders.Add(folder);
                }
            }
            else
            {
                foreach (object item in e.AddedItems)
                    if (item is IStorageFolder folder &&
                        folder.Name != "..")
                        ViewModel.SelectedFolders.Add(folder);

                foreach (object item in e.RemovedItems)
                    if (item is IStorageFolder folder)
                        ViewModel.SelectedFolders.Remove(folder);
            }
        }
    }
}