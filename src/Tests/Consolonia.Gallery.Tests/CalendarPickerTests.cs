using System.Threading.Tasks;
using Avalonia.Input;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class CalendarPickerTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task PerformSingleTest()
        {
            await UITest.AssertHasMatch(@"0?2/16/2022|2022-02-16");
            await UITest.KeyInput(Key.Enter);
            await UITest.AssertHasText("February", "2022", "13 14 15 16 17 18 19");
            await UITest.KeyInput(Key.Right);
            await UITest.AssertHasMatch(@"0?2/17/2022|2022-02-17");
        }
    }
}