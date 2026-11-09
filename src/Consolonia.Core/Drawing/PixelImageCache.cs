using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     A cache of pixel images, keyed by their content, so the same pixels always get the same instance.
    ///     This matters because the pixel buffer compares images by reference. A cell whose pixels did not
    ///     change must keep the same instance, or it is written to the terminal again.
    ///     When the cached pixel bytes exceed the budget, the least recently used images are removed.
    ///     Removal only stops sharing. A cell on screen still holds a reference to its image.
    ///     Thread-safe.
    /// </summary>
    /// <remarks>
    ///     The key is the 128-bit XXH3 hash of the pixels plus the size. In practice two different cells never
    ///     get the same key, so the pixels do not need to be compared. XXH3 is not a cryptographic hash. That is
    ///     fine here, because the pixels come from the app, not from an attacker.
    /// </remarks>
    internal sealed class PixelImageCache
    {
        private readonly Dictionary<ImageKey, LinkedListNode<PixelImage>> _images = new();
        private readonly LinkedList<PixelImage> _recency = new();
        private long _budget;
        private long _bytes;

        /// <param name="budget">The maximum bytes of pixel data to keep before old images are removed.</param>
        public PixelImageCache(long budget)
        {
            _budget = budget;
        }

        /// <summary>Raises the budget to at least <paramref name="budget" />. The budget never shrinks.</summary>
        public void EnsureBudget(long budget)
        {
            lock (_images)
            {
                _budget = Math.Max(_budget, budget);
            }
        }

        /// <summary>
        ///     Looks up the cached image for a pixel hash and size. If found, marks it as most recently used.
        /// </summary>
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
        ///     Adds <paramref name="image" /> unless an image with the same pixels is already cached. After adding,
        ///     removes the least recently used images until the cache is within budget. The newest image is
        ///     never removed.
        /// </summary>
        /// <returns>
        ///     The cached image for these pixels. This is either <paramref name="image" /> or the existing one.
        ///     Callers must use the returned image, so that all cells with the same pixels share one instance.
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