using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Consolonia.Gallery.Tests.Base;
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
        public async Task ThemeMenuSwitchesTheme()
        {
            try
            {
                await UITest.KeyInput(Key.T, RawInputModifiers.Alt);
                await UITest.AssertHasText("Modern", "TurboVision");
                await UITest.KeyInput(Key.Down, Key.Down, Key.Enter);

                await Dispatcher.UIThread.InvokeAsync(() =>
                    Assert.IsInstanceOf<TurboVisionTheme>(Application.Current!.Styles[0]));
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Application.Current!.Styles.RemoveAt(0);
                    Application.Current.Styles.Insert(0, new ModernTheme());
                });

                await UITest.WaitRendered();
            }
        }
    }
}
