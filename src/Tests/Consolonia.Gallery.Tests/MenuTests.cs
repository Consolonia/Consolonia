using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Consolonia.Controls;
using Consolonia.Gallery.Tests.Base;
using Consolonia.Gallery.View;
using Consolonia.NUnit;
using Consolonia.Themes;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class MenuTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            await UITest.AssertHasText("First", "Second");
            await UITest.KeyInput(Key.Enter);
            await UITest.AssertHasMatch("Standard Menu Item", @"Ctrl\+A");
            await UITest.KeyInput(Key.Down, Key.Right);
            await UITest.AssertHasText("Submenu 1");
            await UITest.KeyInput(Key.Escape);
            await UITest.AssertHasNoText("Submenu 1");
            await UITest.KeyInput(Key.Escape);
            await UITest.AssertHasNoText("Standard Menu Item");
        }

        [Test]
        public async Task AltAccessKeyOpensMenu()
        {
            await UITest.AssertHasText("First", "Second");
            await UITest.KeyInput(Key.S, RawInputModifiers.Alt);
            await UITest.AssertHasText("Second Menu Item");
            await UITest.KeyInput(Key.Escape);
            await UITest.AssertHasNoText("Second Menu Item");
            await UITest.KeyInput(Key.Left);
        }

        [Test]
        public async Task ItemTemplateTest()
        {
            await UITest.KeyInput(Key.C, RawInputModifiers.Alt);
            await UITest.AssertHasText("Item:");
            await UITest.AssertHasText("Item 1");
        }

        [TestCase("_File", "Exit")]
        [TestCase("_Theme", "Modern Contrast")]
        [TestCase("_View", "Show XAML")]
        public async Task MainMenuUsesPopupPlacementInsteadOfViewModel(string header, string expectedItem)
        {
            Popup popup = null;
            Decorator decorator = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = (MainWindow)((IClassicDesktopStyleApplicationLifetime)
                    Application.Current.ApplicationLifetime).MainWindow;
                var view = (ControlsListView)window.Content;
                Assert.IsInstanceOf<ModernTheme>(Application.Current.Styles[0]);
                Assert.IsInstanceOf<ControlsListViewModel>(view.DataContext);
                MenuItem menuItem = view.Menu.Items.OfType<MenuItem>()
                    .Single(item => (string)item.Header == header);
                menuItem.IsSubMenuOpen = true;
                popup = menuItem.GetVisualDescendants().OfType<Popup>().Single();
                var panel = (BorderPanel)popup.Child;
                panel.ApplyTemplate();
                Assert.AreSame(view.DataContext, panel.DataContext);
                decorator = panel.GetVisualChildren().OfType<Decorator>().Single();
                Assert.AreEqual(new Thickness(0, -1, 0, 0), decorator.Padding);
            });

            await UITest.WaitRendered();
            await UITest.AssertHasText(expectedItem);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                popup.Placement = PlacementMode.Right;
                Assert.AreEqual(new Thickness(-1, 0, 0, 0), decorator.Padding);
            });
        }
    }
}