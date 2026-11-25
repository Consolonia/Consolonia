using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class TableViewTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            // headers and first rows render
            await UITest.AssertHasMatch("Country.+Region.+Popu.+Area.+GDP",
                "Afghanistan.+ASIA");

            // keyboard selection moves into the rows
            await UITest.KeyInput(Key.Tab, Key.Tab);

            var tableView = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = (MainWindow)((IClassicDesktopStyleApplicationLifetime)
                    Application.Current!.ApplicationLifetime!).MainWindow;
                Assert.IsNotNull(window, "The Gallery should have an initialized main window.");
                return window.GetVisualDescendants().OfType<TableView>().Single();
            });

            await UITest.KeyInput(Key.Down);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Assert.AreEqual(1,
                    tableView.SelectedIndex,
                    "Down should move the row selection from the focused first row to the second row.");
            });

            // headers still render above the selected rows
            await UITest.AssertHasMatch("Country.+Region");

            // scrolling reveals rows further down the list
            await UITest.KeyInput(50 /*todo: Magic number of pagedown*/, Key.PageDown);
            await UITest.AssertHasMatch("Yem");
        }
    }
}
