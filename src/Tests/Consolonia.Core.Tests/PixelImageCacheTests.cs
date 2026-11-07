using System.IO.Hashing;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>The cache the pixel image converter finds the image for a cell's pixels in.</summary>
    [TestFixture]
    public class PixelImageCacheTests
    {
        /// <summary>One pixel, so an image is 4 bytes of the budget.</summary>
        private static PixelImage Image(byte value)
        {
            byte[] bgra = { value, value, value, 0xFF };
            return new PixelImage(bgra, 0, 1, 1, XxHash128.HashToUInt128(bgra));
        }

        private static bool Holds(PixelImageCache cache, PixelImage image)
        {
            return cache.TryGet(image.Digest, image.Width, image.Height, out PixelImage held) &&
                   ReferenceEquals(held, image);
        }

        [Test]
        public void TheSamePixelsGetTheImageAlreadyHeld()
        {
            var cache = new PixelImageCache(1024);
            PixelImage first = Image(1);

            Assert.That(cache.GetOrAdd(first), Is.SameAs(first));
            Assert.That(cache.GetOrAdd(Image(1)), Is.SameAs(first), "the image already held is returned");
            Assert.That(cache.TryGet(Image(2).Digest, 1, 1, out _), Is.False, "other pixels are not found");
        }

        [Test]
        public void TheCacheEvictsBeyondItsBudgetAndARaisedBudgetHoldsMore()
        {
            var cache = new PixelImageCache(8);
            PixelImage one = Image(1);
            PixelImage two = Image(2);
            PixelImage three = Image(3);

            cache.GetOrAdd(one);
            cache.GetOrAdd(two);
            cache.GetOrAdd(three);
            Assert.That(Holds(cache, one), Is.False, "the least recently used goes");
            Assert.That(Holds(cache, two) && Holds(cache, three), Is.True);

            cache.EnsureBudget(40);
            PixelImage four = Image(4);
            PixelImage five = Image(5);
            cache.GetOrAdd(four);
            cache.GetOrAdd(five);
            Assert.That(Holds(cache, two) && Holds(cache, five), Is.True, "nothing more once the budget is raised");

            cache.EnsureBudget(4);
            cache.GetOrAdd(Image(6));
            Assert.That(Holds(cache, two), Is.True, "the budget never shrinks");
        }

        [Test]
        public void TheSameDigestAtAnotherSizeIsAnotherImage()
        {
            var cache = new PixelImageCache(1024);
            PixelImage image = Image(1);
            cache.GetOrAdd(image);

            Assert.That(cache.TryGet(image.Digest, 2, 1, out _), Is.False);
        }
    }
}