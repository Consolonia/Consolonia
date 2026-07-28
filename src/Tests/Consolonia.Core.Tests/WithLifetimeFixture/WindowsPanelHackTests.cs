using System.Linq;
using Avalonia;
using Iciclecreek.Avalonia.WindowManager;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public class WindowsPanelHackTests
    {
        // Checks that hack A6172E10-6B6C-414B-AFE4-C84C6B84462D is still needed.
        // WindowsPanel's static constructor inserts a WindowManagerTheme into
        // Application.Current.Styles. The hack pre-initializes WindowsPanel so that
        // the application styles are not modified afterwards. If creating a
        // WindowsPanel no longer adds WindowManagerTheme to the application styles,
        // this hack is not needed anymore.
        [Test]
        public void WindowsPanelStillModifiesApplicationStyles()
        {
            Assert.IsNotNull(Application.Current, "Application must be initialized for this test");

            _ = new WindowsPanel();

            bool hasTheme = Application.Current!.Styles.OfType<WindowManagerTheme>().Any();
            Assert.IsTrue(hasTheme,
                "WindowManagerTheme was not added to Application.Current.Styles. " +
                "Hack A6172E10-6B6C-414B-AFE4-C84C6B84462D is not needed anymore.");
        }
    }
}