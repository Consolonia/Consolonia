using System;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    /// <summary>
    ///     One cell's slice of a kitty image, carried as part of the cell's BACKGROUND.
    /// </summary>
    /// <remarks>
    ///     This is what makes "text over a picture" possible: the image is background, the glyph is
    ///     foreground, and they composite. Writing an opaque background color over the cell evicts
    ///     the tile (the background owns the image's lifetime); writing a glyph with a transparent
    ///     background leaves it in place. The renderer emits contiguous tiles as classic kitty
    ///     placements at a negative z-index, which the terminal draws below text.
    /// </remarks>
    public readonly struct KittyTile(int imageId, ushort x, ushort y) : IEquatable<KittyTile>
    {
        public static KittyTile None => default;

#pragma warning disable CA1051 // Do not declare visible instance fields
        /// <summary>The kitty image id, or 0 for no tile.</summary>
        public readonly int ImageId = imageId;

        /// <summary>Tile column within the image's cell grid.</summary>
        public readonly ushort X = x;

        /// <summary>Tile row within the image's cell grid.</summary>
        public readonly ushort Y = y;
#pragma warning restore CA1051 // Do not declare visible instance fields

        public bool IsEmpty => ImageId == 0;

        public bool Equals(KittyTile other)
        {
            return ImageId == other.ImageId && X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is KittyTile other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ImageId, X, Y);
        }

        public static bool operator ==(KittyTile left, KittyTile right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(KittyTile left, KittyTile right)
        {
            return !left.Equals(right);
        }
    }
}
