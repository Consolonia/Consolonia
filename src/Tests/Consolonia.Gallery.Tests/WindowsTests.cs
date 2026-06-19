using System.Threading.Tasks;
using System.Linq;
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
    internal class WindowsTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            await FocusButton("Add Window");
            await UITest.KeyInput(Key.Enter); // add window
            await Task.Delay(100); // animation
            await UITest.AssertHasText("New Window 1");
            await UITest.AssertHasText("🗕", "🗖", "🗙");
            await UITest.AssertHasNoText("🗗");
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Enter); // close button
            await Task.Delay(100); // animation
            foreach (string x in new[] { "New Window 1", "DialogResult:", "🗕", "🗖", "🗙" })
                await UITest.AssertHasNoText(x);
        }

        private static async Task FocusButton(string content)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var mainWindow =
                    (Window)
                    ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime)!.MainWindow!;
                Button button = mainWindow.GetVisualDescendants()
                    .OfType<Button>()
                    .First(x => x.Content?.ToString() == content);
                button.Focus(NavigationMethod.Tab);
            });

            await UITest.WaitRendered();
        }
    }
}
