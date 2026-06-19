using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Consolonia.Gallery.Gallery.GalleryViews
{
    public partial class GalleryDragAndDrop : UserControl
    {
        private readonly DataFormat<string> _customFormat =
            DataFormat.CreateStringApplicationFormat("xxx-avalonia-controlcatalog-custom");

        private readonly TextBlock _dropState;

        public GalleryDragAndDrop()
        {
            InitializeComponent();
            _dropState = this.Get<TextBlock>("DropState");

            int textCount = 0;

            SetupDnd(
                "Text",
                d => d.Add(DataTransferItem.CreateText($"Text was dragged {++textCount} times")),
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

            SetupDnd(
                "Custom",
                d => d.Add(DataTransferItem.Create(_customFormat, "Test123")),
                DragDropEffects.Move);

            SetupDnd(
                "Files",
                async d =>
                {
                    string path = Path.GetTempFileName();
                    d.Add(DataTransferItem.CreateFile(await TopLevel.GetTopLevel(this).StorageProvider
                        .TryGetFileFromPathAsync(path)));
                },
                DragDropEffects.Copy);
        }

        private void SetupDnd(string suffix, Action<DataTransfer> factory, DragDropEffects effects)
        {
            SetupDnd(
                suffix,
                o =>
                {
                    factory(o);
                    return Task.CompletedTask;
                },
                effects);
        }

        private void SetupDnd(string suffix, Func<DataTransfer, Task> factory, DragDropEffects effects)
        {
            var dragMe = this.Get<Border>("DragMe" + suffix);
            var dragState = this.Get<TextBlock>("DragState" + suffix);

            async void DoDrag(object sender, PointerPressedEventArgs e)
            {
                var dragData = new DataTransfer();
                await factory(dragData);

                DragDropEffects result = await DragDrop.DoDragDropAsync(e, dragData, effects);
                switch (result)
                {
                    case DragDropEffects.Move:
                        dragState.Text = "Data was moved";
                        break;
                    case DragDropEffects.Copy:
                        dragState.Text = "Data was copied";
                        break;
                    case DragDropEffects.Link:
                        dragState.Text = "Data was linked";
                        break;
                    case DragDropEffects.None:
                        dragState.Text = "The drag operation was canceled";
                        break;
                    default:
                        dragState.Text = "Unknown result";
                        break;
                }
            }

            void DragOver(object sender, DragEventArgs e)
            {
                if (e.Source is Control c && c.Name == "MoveTarget")
                    e.DragEffects = e.DragEffects & DragDropEffects.Move;
                else
                    e.DragEffects = e.DragEffects & DragDropEffects.Copy;

                // Only allow if the dragged data contains text or filenames.
                if (!e.DataTransfer.Formats.Contains(DataFormat.Text)
                    && !e.DataTransfer.Formats.Contains(DataFormat.File)
                    && !e.DataTransfer.Formats.Contains(_customFormat))
                    e.DragEffects = DragDropEffects.None;
            }

            async void Drop(object sender, DragEventArgs e)
            {
                if (e.Source is Control c && c.Name == "MoveTarget")
                    e.DragEffects = e.DragEffects & DragDropEffects.Move;
                else
                    e.DragEffects = e.DragEffects & DragDropEffects.Copy;

                if (e.DataTransfer.Formats.Contains(DataFormat.Text))
                {
                    _dropState.Text = e.DataTransfer.TryGetText();
                }
                else if (e.DataTransfer.Formats.Contains(DataFormat.File))
                {
                    IStorageItem[] files = e.DataTransfer.TryGetValues(DataFormat.File) ?? Array.Empty<IStorageItem>();
                    string contentStr = "";

                    foreach (IStorageItem item in files)
                        if (item is IStorageFile file)
                        {
                            // var content = await DialogsPage.ReadTextFromFile(file, 500);
                            string content = "text";
                            contentStr +=
                                $"File {item.Name}:{Environment.NewLine}{content}{Environment.NewLine}{Environment.NewLine}";
                        }
                        else if (item is IStorageFolder folder)
                        {
                            int childrenCount = 0;
                            await foreach (IStorageItem _ in folder.GetItemsAsync()) childrenCount++;
                            contentStr +=
                                $"Folder {item.Name}: items {childrenCount}{Environment.NewLine}{Environment.NewLine}";
                        }

                    _dropState.Text = contentStr;
                }
                else if (e.DataTransfer.Formats.Contains(_customFormat))
                {
                    string value = e.DataTransfer.Items
                        .Select(item => item.TryGetRaw(_customFormat))
                        .OfType<string>()
                        .FirstOrDefault();
                    _dropState.Text = "Custom: " + value;
                }
            }

            dragMe.PointerPressed += DoDrag;

            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}