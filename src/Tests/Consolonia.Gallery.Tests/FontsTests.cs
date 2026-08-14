using System.Threading.Tasks;
using Avalonia.Input;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    /// <summary>
    ///     Unit test for TextBlock view
    /// </summary>
    [TestFixture]
    internal class FontsTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task DisplaysBasicText()
        {
            await UITest.AssertHasText("Hello World!");
        }


        [Test]
        public async Task DisplaysWideTermText()
        {
            await UITest.KeyInput(Key.Down);
            await UITest.AssertHasText("Ｈｅｌｌｏ  Ｗｏｒｌｄ！");
        }


        [Test]
        public async Task DisplaysBrailleText()
        {
            await UITest.KeyInput(2, Key.Down);

            await UITest.AssertHasText(
                "⣇⣸ ⢀⡀ ⡇ ⡇ ⢀⡀   ⡇⢸ ⢀⡀ ⡀⣀ ⡇ ⢀⣸ ⡇",
                "⠇⠸ ⠣⠭ ⠣ ⠣ ⠣⠜   ⠟⠻ ⠣⠜ ⠏  ⠣ ⠣⠼ ⠅");
        }


        [Test]
        public async Task DisplaysCircleText()
        {
            await UITest.KeyInput(3, Key.Down);
            await UITest.AssertHasText("Circle");
        }


        [Test]
        public async Task DisplaysDoomText()
        {
            await UITest.KeyInput(6, Key.Down);
            await UITest.AssertHasText(
                @" _   _       _ _         _    _             _     _ ",
                @"| | | |     | | |       | |  | |           | |   | |",
                @"| |_| | ___ | | | ___   | |  | |  ___  _ __| | __| |",
                @"|  _  |/ _ \| | |/ _ \  | |/\| | / _ \| '__| |/ _` |",
                @"| | | |  __/| | | (_) | \  /\  /| (_) | |  | | (_| |",
                @"\_| |_/\___||_|_|\___/   \/  \/  \___/|_|  |_|\__,_|"
            );
        }
    }
}
