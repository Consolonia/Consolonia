using System.Threading.Tasks;
using Avalonia.Input;
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
            await UITest.KeyInput(Key.Tab);
            // await UITest.KeyInput(Key.Down); // change startup to be centerowner
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Down); // change startup to be maximized
            await UITest.KeyInput(Key.Down); // change startup to be maximized
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Enter); // add window
            await Task.Delay(100); // animation
            await UITest.AssertHasText("New Window 1");
            await UITest.AssertHasText("🗕", "🗗", "🗙");
            await UITest.AssertHasNoText("🗖");
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Enter); // close button
            await Task.Delay(100); // animation
            foreach (string x in new[] { "New Window 1", "DialogResult:", "🗕", "🗗", "🗙" })
                await UITest.AssertHasNoText(x);
        }
    }
}