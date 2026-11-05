using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     The pixel images made so far, by what they show, so the same pixels always get the same
    ///     instance: the pixel buffer compares images by reference, and a cell whose pixels did not change
    ///     must keep its image to not be written again. Least recently used first out beyond a budget of
    ///     pixel bytes; eviction only ends the sharing, a cell on screen holds its image by reference.
    /// </summary>
    /// <remarks>
    ///     Found by the 128-bit XXH3 digest of the pixels and the size, so two different cells never share
    ///     a key in practice and nothing needs keeping to compare against. It is not cryptographic, which is
    ///     fine: the pixels are the app's own, not an adversary's.
    /// </remarks>
    internal sealed class PixelImageCache
    {
        private readonly Dictionary<ImageKey, LinkedListNode<PixelImage>> _images = new();
        private readonly LinkedList<PixelImage> _recency = new();
        private long _budget;
        private long _bytes;

        /// <param name="budget">Pixel bytes to hold before the least recently used are dropped.</param>
        public PixelImageCache(long budget)
        {
            _budget = budget;
        }

        /// <summary>Raises the budget to at least <paramref name="budget" />; it never shrinks.</summary>
        public void EnsureBudget(long budget)
        {
            lock (_images)
            {
                _budget = Math.Max(_budget, budget);
            }
        }

        /// <summary>The image made for these pixels, marked most recently used.</summary>
        public bool TryGet(UInt128 digest, int width, int height, out PixelImage image)
        {
            lock (_images)
            {
                if (_images.TryGetValue(new ImageKey(digest, width, height), out LinkedListNode<PixelImage> node))
                {
                    _recency.Remove(node);
                    _recency.AddFirst(node);
                    image = node.Value;
                    return true;
                }

                image = null;
                return false;
            }
        }

        /// <summary>
        ///     Holds <paramref name="image" /> unless one for the same pixels is already held, then drops the
        ///     least recently used until the bytes held are back within budget. The newest is never dropped.
        /// </summary>
        /// <returns>
        ///     The image held for these pixels: <paramref name="image" />, or the one already there. Callers
        ///     keep THAT one, so every cell showing the same pixels shares one instance and compares equal.
        /// </returns>
        public PixelImage GetOrAdd(PixelImage image)
        {
            lock (_images)
            {
                ref LinkedListNode<PixelImage> slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_images,
                    new ImageKey(image.Digest, image.Width, image.Height), out bool held);
                if (held)
                    return slot.Value;

                slot = _recency.AddFirst(image);
                _bytes += Bytes(image);

                while (_bytes > _budget && _recency.Count > 1)
                {
                    PixelImage oldest = _recency.Last!.Value;
                    _recency.RemoveLast();
                    _images.Remove(new ImageKey(oldest.Digest, oldest.Width, oldest.Height));
                    _bytes -= Bytes(oldest);
                }

                return image;
            }
        }

        private static long Bytes(PixelImage image)
        {
            return 4L * image.Width * image.Height;
        }

        private readonly record struct ImageKey(UInt128 Digest, int Width, int Height);
    }
}
