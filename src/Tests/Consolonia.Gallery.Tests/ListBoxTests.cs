using System.Threading.Tasks;
using Avalonia.Input;
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