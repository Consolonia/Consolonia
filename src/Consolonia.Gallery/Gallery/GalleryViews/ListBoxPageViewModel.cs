#pragma warning disable CA5394 // Do not use insecure randomness
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using AvaloniaSelectionMode = Avalonia.Controls.SelectionMode;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public class ListBoxPageViewModel : ViewModelBase
    {
        private bool _alwaysSelected;
        private bool _autoScrollToSelectedItem = true;
        private int _counter;
        private bool _multiple;
        private bool _toggle;

        public ListBoxPageViewModel()
        {
            Items = new ObservableCollection<string>(Enumerable.Range(1, 10000).Select(_ => GenerateItem()));

            Selection = new SelectionModel<string>();
            Selection.Select(1);

            AddItemCommand = MiniCommand.Create(() => Items.Add(GenerateItem()));

            RemoveItemCommand = MiniCommand.Create(() =>
            {
                List<string> items = Selection.SelectedItems.ToList();

                foreach (string item in items) Items.Remove(item);
            });

            SelectRandomItemCommand = MiniCommand.Create(() =>
            {
                using (Selection.BatchUpdate())
                {
                    Selection.Clear();
                    Selection.Select(Random.Shared.Next(Items.Count - 1));
                }
            });
        }

        public ObservableCollection<string> Items { get; }
        public SelectionModel<string> Selection { get; }
        public AvaloniaSelectionMode SelectionMode =>
            (Multiple ? AvaloniaSelectionMode.Multiple : 0) |
            (Toggle ? AvaloniaSelectionMode.Toggle : 0) |
            (AlwaysSelected ? AvaloniaSelectionMode.AlwaysSelected : 0);

        public bool Multiple
        {
            get => _multiple;
            set
            {
                if (RaiseAndSetIfChanged(ref _multiple, value))
                    RaisePropertyChanged(nameof(SelectionMode));
            }
        }

        public bool Toggle
        {
            get => _toggle;
            set
            {
                if (RaiseAndSetIfChanged(ref _toggle, value))
                    RaisePropertyChanged(nameof(SelectionMode));
            }
        }

        public bool AlwaysSelected
        {
            get => _alwaysSelected;
            set
            {
                if (RaiseAndSetIfChanged(ref _alwaysSelected, value))
                    RaisePropertyChanged(nameof(SelectionMode));
            }
        }

        public bool AutoScrollToSelectedItem
        {
            get => _autoScrollToSelectedItem;
            set => RaiseAndSetIfChanged(ref _autoScrollToSelectedItem, value);
        }

        public MiniCommand AddItemCommand { get; }
        public MiniCommand RemoveItemCommand { get; }
        public MiniCommand SelectRandomItemCommand { get; }

        private string GenerateItem()
        {
            return $"Item {_counter++}";
        }
    }
}