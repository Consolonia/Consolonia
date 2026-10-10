using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     A picture's rendering is kept per size and refilled in place for each version and visible part,
    ///     so an edit allocates no screen-sized buffers.
    /// </summary>
    [TestFixture]
    public class CellRenderingTests
    {
        [Test]
        public void TheBuffersAreKeptWhileTheirSizeIsUnchanged()
        {
            var rendering = new CellRendering();
            rendering.Prepare(new PixelRect(0, 0, 4, 2), 8, 16);
            byte[] pixels = rendering.Pixels;
            PixelBuffer cells = rendering.Cells;
            Assert.That(pixels.Length, Is.EqualTo(4 * 8 * 2 * 16 * 4));
            Assert.That(cells.Width, Is.EqualTo(4));
            Assert.That(cells.Height, Is.EqualTo(2));

            rendering.Prepare(new PixelRect(3, 1, 4, 2), 8, 16);
            Assert.That(rendering.Pixels, Is.SameAs(pixels), "a scroll keeps the pixel buffer");
            Assert.That(rendering.Cells, Is.SameAs(cells), "a scroll keeps the cells");

            rendering.Prepare(new PixelRect(0, 0, 5, 2), 8, 16);
            Assert.That(rendering.Pixels, Is.Not.SameAs(pixels));
            Assert.That(rendering.Pixels.Length, Is.EqualTo(5 * 8 * 2 * 16 * 4));
            Assert.That(rendering.Cells.Width, Is.EqualTo(5));
        }

        [Test]
        public void ARenderingShowsAVersionOnlyOnceComplete()
        {
            var rendering = new CellRendering();
            var visible = new PixelRect(0, 0, 2, 2);
            Assert.That(rendering.Shows(1, visible), Is.False, "never rendered");

            rendering.Prepare(visible, 8, 16);
            Assert.That(rendering.Shows(1, visible), Is.False, "prepared but not rendered");

            rendering.Complete(1);
            Assert.That(rendering.Shows(1, visible), Is.True);
            Assert.That(rendering.Shows(2, visible), Is.False, "an edit");
            Assert.That(rendering.Shows(1, new PixelRect(1, 0, 2, 2)), Is.False, "a scroll");

            rendering.Prepare(visible, 8, 16);
            Assert.That(rendering.Shows(1, visible), Is.False,
                "being rendered again: a render that throws must not leave it claiming the old version");
        }

        [Test]
        public void OneRenderingPerSizeTheLeastRecentlyDrawnGoingBeyondFour()
        {
            var renderings = new List<KeyValuePair<RenderingKey, CellRendering>>();
            CellRendering first = CellBitmapRenderer<CellRendering>.FindOrAdd(renderings, Key(1));
            Assert.That(CellBitmapRenderer<CellRendering>.FindOrAdd(renderings, Key(1)), Is.SameAs(first),
                "the same size is the same rendering, whatever the version");

            for (int size = 2; size <= 4; size++)
                CellBitmapRenderer<CellRendering>.FindOrAdd(renderings, Key(size));
            Assert.That(renderings.Count, Is.EqualTo(4));

            CellBitmapRenderer<CellRendering>.FindOrAdd(renderings, Key(1)); // drawn again: the most recent now
            CellBitmapRenderer<CellRendering>.FindOrAdd(renderings, Key(5));
            Assert.That(renderings.Count, Is.EqualTo(4));
            Assert.That(renderings.ConvertAll(r => r.Key.TargetSize.Width / 8), Is.EqualTo(new[] { 3, 4, 1, 5 }),
                "size 2, the least recently drawn, is gone");
            Assert.That(CellBitmapRenderer<CellRendering>.FindOrAdd(renderings, Key(1)), Is.SameAs(first));
        }

        private static RenderingKey Key(int size)
        {
            return new RenderingKey(new PixelSize(size * 8, 16), BitmapInterpolationMode.None);
        }
    }
}
