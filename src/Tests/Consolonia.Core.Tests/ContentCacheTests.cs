using System;
using System.Collections.Generic;
using Consolonia.Core.Drawing;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>The content cache the cell image renderer and the sixel encoder keep their work in.</summary>
    [TestFixture]
    public class ContentCacheTests
    {
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

        private static ContentKey Key(int value)
        {
            return ContentKey.Of(BitConverter.GetBytes(value), 1, 1);
        }
    }
}