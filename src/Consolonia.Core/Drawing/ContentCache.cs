using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Identifies a block of pixels by what it shows: a SHA-256 digest of its BGRA bytes and its size.
    /// </summary>
    /// <remarks>
    ///     A cryptographic digest, so two different blocks never share a key in practice and nothing
    ///     needs keeping to compare against; SHA-256 is hardware accelerated, a screenful hashes in
    ///     a few milliseconds.
    /// </remarks>
    internal readonly record struct ContentKey(UInt128 Digest, int Width, int Height)
    {
        public static ContentKey Of(ReadOnlySpan<byte> bgra, int width, int height)
        {
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(bgra, digest);
            return new ContentKey(MemoryMarshal.Read<UInt128>(digest), width, height);
        }
    }

    /// <summary>
    ///     Least-recently-used cache of what was made from blocks of pixels, so a block seen before is
    ///     not encoded, or sent to the terminal, again. Holds up to a budget of cost, dropping the
    ///     least recently used beyond it.
    /// </summary>
    internal sealed class ContentCache<T>
    {
        private readonly long _budget;
        private readonly Action<T> _evicted;
        private readonly Dictionary<ContentKey, LinkedListNode<Entry>> _entries = new();
        private readonly LinkedList<Entry> _recency = new();
        private long _cost;

        /// <param name="budget">Total cost to hold before the least recently used are dropped.</param>
        /// <param name="evicted">Called with each value dropped, to free what it holds.</param>
        public ContentCache(long budget, Action<T> evicted = null)
        {
            _budget = budget;
            _evicted = evicted;
        }

        /// <summary>The value made for <paramref name="key" />, marked most recently used.</summary>
        public bool TryGet(ContentKey key, out T value)
        {
            lock (_entries)
            {
                if (_entries.TryGetValue(key, out LinkedListNode<Entry> node))
                {
                    _recency.Remove(node);
                    _recency.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }

                value = default;
                return false;
            }
        }

        /// <summary>
        ///     Holds <paramref name="value" /> for <paramref name="key" />, then drops the least recently
        ///     used until the total cost is back within budget. The newest is never dropped.
        /// </summary>
        public void Add(ContentKey key, T value, long cost)
        {
            lock (_entries)
            {
                ref LinkedListNode<Entry> slot =
                    ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, key, out bool held);
                if (held)
                    return;

                slot = _recency.AddFirst(new Entry(key, value, cost));
                _cost += cost;

                while (_cost > _budget && _recency.Count > 1)
                {
                    LinkedListNode<Entry> oldest = _recency.Last!;
                    _recency.RemoveLast();
                    _entries.Remove(oldest.Value.Key);
                    _cost -= oldest.Value.Cost;
                    _evicted?.Invoke(oldest.Value.Value);
                }
            }
        }

        private readonly record struct Entry(ContentKey Key, T Value, long Cost);
    }
}
