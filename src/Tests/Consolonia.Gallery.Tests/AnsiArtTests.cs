using System.Threading.Tasks;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Gallery.Tests.Base;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Gallery.Tests
{
    [TestFixture]
    internal class AnsiArtTests : GalleryTestsBaseBase
    {
        public AnsiArtTests() : base(new PixelBufferSize(160, 80))
        {
        }

        [Test]
        public async Task PerformSingleTest()
        {
            await UITest.AssertHasText("ANSI Art Rendering",
                "Original Size",
                "Uniform Scaling",
                "Fill Scaling");

            // Verify that a line from the middle of the ANSI art picture has been rendered
            await UITest.AssertHasText("▀▄▄▀/▓Nachos!▓▒░");
        }
    }
}
