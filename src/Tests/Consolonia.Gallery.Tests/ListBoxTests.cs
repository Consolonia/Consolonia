using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Gallery.Gallery.GalleryViews;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;
using AvaloniaSelectionMode = Avalonia.Controls.SelectionMode;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class ListBoxTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            await UITest.AssertHasNoText("Item 27");

            await UITest.KeyInput(Key.PageDown);
            await UITest.AssertHasText("Item 26");
        }

        [Test]
        public async Task HeaderTextBlocksRenderOnAdjacentRows()
        {
            string[] headerLines =
            [
                "ListBox",
                "Hosts a collection of ListBoxItem.",
                "Each 5th item is highlighted with nth-child"
            ];

            await UITest.AssertHasText(headerLines);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                string[] screenLines = UITest.PixelBuffer.PrintBuffer()
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int previousRow = -1;

                foreach (string headerLine in headerLines)
                {
                    int row = Array.FindIndex(screenLines,
                        line => line.Contains(headerLine, StringComparison.Ordinal));
                    Assert.GreaterOrEqual(row, 0, $"Could not find '{headerLine}' in the rendered screen.");

                    if (previousRow >= 0)
                        Assert.AreEqual(previousRow + 1, row,
                            $"Expected '{headerLine}' to render directly below the previous header line.");

                    previousRow = row;
                }
            });
        }

        [Test]
        public void SelectionModeReflectsOptionFlags()
        {
            var viewModel = new ListBoxPageViewModel();
            var selectionModeChanged = false;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ListBoxPageViewModel.SelectionMode))
                    selectionModeChanged = true;
            };

            viewModel.Multiple = true;
            viewModel.Toggle = true;
            viewModel.AlwaysSelected = true;

            Assert.IsTrue(selectionModeChanged);
            Assert.AreEqual(
                AvaloniaSelectionMode.Multiple |
                AvaloniaSelectionMode.Toggle |
                AvaloniaSelectionMode.AlwaysSelected,
                viewModel.SelectionMode);
        }
    }
}
