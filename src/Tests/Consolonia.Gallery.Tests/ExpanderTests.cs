using System.Threading.Tasks;
using Avalonia.Input;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class ExpanderTests : GalleryTestsBaseBase
    {
        [Test]
        public async Task TestStart()
        {
            await UITest.AssertHasMatch("Up.*▴", @"Down.*▾", @"⏴.*Left", @"Right.*⏵");
            await UITest.AssertHasNoText("Top content");
            await UITest.AssertHasNoText("Right content");
            await UITest.AssertHasNoText("Left content");
            await UITest.AssertHasNoText("Bottom content");
        }

        [Test]
        public async Task TestUp()
        {
            await UITest.AssertHasMatch("Up.*▴");
            await UITest.AssertHasNoText("Top content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("Up.*▾");
            await UITest.AssertHasText("Top content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("Up.*▴");
            await UITest.AssertHasNoText("Top content");
        }

        [Test]
        public async Task TestDown()
        {
            await UITest.KeyInput(Key.Tab);
            await UITest.AssertHasMatch("Down.*▾");
            await UITest.AssertHasNoText("Bottom content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("Down.*▴");
            await UITest.AssertHasText("Bottom content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("Down.*▾");
            await UITest.AssertHasNoText("Bottom content");
        }

        [Test]
        public async Task TestLeft()
        {
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.AssertHasMatch("⏴.*Left");
            await UITest.AssertHasNoText("Left content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("Left.*⏵");
            await UITest.AssertHasText("Left content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("⏴.*Left");
            await UITest.AssertHasNoText("Left content");
        }

        [Test]
        public async Task TestRight()
        {
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.KeyInput(Key.Tab);
            await UITest.AssertHasMatch("Right.*⏵");
            await UITest.AssertHasNoText("Right content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("⏴.*Right");
            await UITest.AssertHasText("Right content");

            await UITest.KeyInput(Key.Space);
            await UITest.AssertHasMatch("Right.*⏵");
            await UITest.AssertHasNoText("Right content");
        }
    }
}
