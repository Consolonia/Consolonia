using System.Collections.Generic;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace Consolonia.Imaging
{
    /// <summary>
    ///     The sixel bytes rendered for runs of cells, by what the run shows, so a run seen before is not
    ///     quantized and encoded again: a modal closing over a picture re-sends the runs it dimmed from
    ///     here. Least recently used first out beyond a budget of bytes.
    /// </summary>
    internal sealed class RenderedRunCache
    {
        private readonly long _budget;
        private readonly Dictionary<PictureKey, LinkedListNode<Entry>> _entries = new();
        private readonly LinkedList<Entry> _recency = new();
        private long _bytes;

        public RenderedRunCache(long budget)
        {
            _budget = budget;
        }

        /// <summary>The bytes rendered for <paramref name="key" />, marked most recently used.</summary>
        public bool TryGet(PictureKey key, out byte[] rendered)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<Entry> node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                rendered = node.Value.Rendered;
                return true;
            }

            rendered = null;
            return false;
        }

        /// <summary>
        ///     Holds <paramref name="rendered" /> for <paramref name="key" /> unless bytes are already held for
        ///     it, then drops the least recently used until the bytes held are back within budget. The
        ///     newest is never dropped.
        /// </summary>
        public void Add(PictureKey key, byte[] rendered)
        {
            ref LinkedListNode<Entry> slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, key, out bool held);
            if (held)
                return;

            slot = _recency.AddFirst(new Entry(key, rendered));
            _bytes += rendered.Length;

            while (_bytes > _budget && _recency.Count > 1)
            {
                Entry oldest = _recency.Last!.Value;
                _recency.RemoveLast();
                _entries.Remove(oldest.Key);
                _bytes -= oldest.Rendered.Length;
            }
        }

        private readonly record struct Entry(PictureKey Key, byte[] Rendered);
    }
}
