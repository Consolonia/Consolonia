using System.Threading.Tasks;
using Avalonia.Input;
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
            await UITest.KeyInput(Key.Down);
            await UITest.AssertHasMatch("Country.+Region");

            // scrolling reveals rows further down the list
            await UITest.KeyInput(50 /*todo: Magic number of pagedown*/, Key.PageDown);
            await UITest.AssertHasMatch("Yem");
        }
    }
}
