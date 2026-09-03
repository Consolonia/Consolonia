using System.Threading.Tasks;
using Avalonia.Input;
using Consolonia.Gallery.Gallery.GalleryViews;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class DialogTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            await UITest.KeyInput(Key.Enter);
            await UITest.KeyInput(Key.Tab);
            // The dialog opens and paints asynchronously; poll so slow CI runners
            // don't assert before its content has been laid out and rendered.
            await UITest.WaitForText(SomeDialogWindow.DialogTitle);
            await UITest.WaitForText("One More");
            await UITest.KeyInput(Key.Escape);
            // The dialog closes asynchronously after Escape; poll instead of a fixed
            // delay so slow CI runners don't fail while the close is still in flight.
            await UITest.WaitForNoText("One More");
        }
    }
}