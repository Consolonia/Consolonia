using System.Collections.Generic;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     When kitty images leave the terminal: a tile image evicted from the renderer's cache is deleted
    ///     once no placement shows it, and not before.
    /// </summary>
    [TestFixture]
    public class KittyImageLifetimeTests
    {
        [Test]
        public void AnEvictedImageNothingShowsIsAnOrphan()
        {
            var evicted = new HashSet<int> { 1, 2, 3 };
            var orphans = new List<int>();

            RenderTarget.SelectOrphans(evicted, new HashSet<int> { 2 }, orphans);

            Assert.That(orphans, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(evicted, Is.EquivalentTo(new[] { 2 }), "the image still shown waits");
        }

        [Test]
        public void AnEvictedImageStillShownIsDeletedOnceItsPlacementGoes()
        {
            var evicted = new HashSet<int> { 7 };
            var orphans = new List<int>();

            RenderTarget.SelectOrphans(evicted, new HashSet<int> { 7 }, orphans);
            Assert.That(orphans, Is.Empty, "deleting it now would leave a hole in a picture on screen");

            RenderTarget.SelectOrphans(evicted, new HashSet<int>(), orphans);
            Assert.That(orphans, Is.EquivalentTo(new[] { 7 }));
            Assert.That(evicted, Is.Empty);
        }

        [Test]
        public void TheCacheEvictsBeyondItsBudgetAndARaisedBudgetHoldsMore()
        {
            var evicted = new List<int>();
            var cache = new ContentCache<int>(2, evicted.Add);

            Assert.That(cache.GetOrAdd(Key(1), 1, 1), Is.EqualTo(1));
            Assert.That(cache.GetOrAdd(Key(1), 99, 1), Is.EqualTo(1), "the value already held is returned");
            cache.GetOrAdd(Key(2), 2, 1);
            cache.GetOrAdd(Key(3), 3, 1);
            Assert.That(evicted, Is.EquivalentTo(new[] { 1 }), "the least recently used goes");

            cache.EnsureBudget(10);
            cache.GetOrAdd(Key(4), 4, 1);
            cache.GetOrAdd(Key(5), 5, 1);
            Assert.That(evicted, Is.EquivalentTo(new[] { 1 }), "nothing more once the budget is raised");

            cache.EnsureBudget(1);
            cache.GetOrAdd(Key(6), 6, 1);
            Assert.That(evicted, Is.EquivalentTo(new[] { 1 }), "the budget never shrinks");
        }

        [Test]
        public void ClearingTheCacheDropsEverythingWithoutEvicting()
        {
            var evicted = new List<int>();
            var cache = new ContentCache<int>(10, evicted.Add);
            cache.GetOrAdd(Key(1), 1, 1);
            cache.GetOrAdd(Key(2), 2, 1);

            cache.Clear();

            Assert.That(cache.TryGet(Key(1), out _), Is.False);
            Assert.That(cache.TryGet(Key(2), out _), Is.False);
            Assert.That(evicted, Is.Empty, "the caller deletes them itself, or the terminal lost them");
            Assert.That(cache.GetOrAdd(Key(1), 11, 1), Is.EqualTo(11), "made again");
        }

        [Test]
        public void ASixelSymbolIsSomethingToDraw()
        {
            var sixel = new Sixel(new byte[] { 0, 0, 0, 255 }, 1, new byte[8 * 16], 8, 16, 8, 16);

            Assert.That(new Symbol(sixel).NothingToDraw(), Is.False);
            Assert.That(Symbol.Empty.NothingToDraw(), Is.True);
        }

        private static ContentKey Key(int value)
        {
            return ContentKey.Of(System.BitConverter.GetBytes(value), 1, 1);
        }
    }
}
