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
        [Order(0)]
        public async Task DisplaysBasicText()
        {
            await UITest.AssertHasText("Hello World!");
        }


        [Test]
        [Order(1)]
        public async Task DisplaysWideTermText()
        {
            await UITest.KeyInput(Key.Down);
            await UITest.AssertHasText("Ｈｅｌｌｏ  Ｗｏｒｌｄ！");
        }


        [Test]
        [Order(2)]
        public async Task DisplaysBrailleText()
        {
            await UITest.KeyInput(Key.Down);

            await UITest.AssertHasText(
                "⣇⣸ ⢀⡀ ⡇ ⡇ ⢀⡀   ⡇⢸ ⢀⡀ ⡀⣀ ⡇ ⢀⣸ ⡇",
                "⠇⠸ ⠣⠭ ⠣ ⠣ ⠣⠜   ⠟⠻ ⠣⠜ ⠏  ⠣ ⠣⠼ ⠅");
        }


        [Test]
        [Order(3)]
        public async Task DisplaysDoomText()
        {
            await UITest.KeyInput(4, Key.Down);
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