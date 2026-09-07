using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    [DebuggerDisplay("[{Color}]")]
    [JsonConverter(typeof(PixelBackgroundConverter))]
    public readonly struct PixelBackground(Color color) : IEquatable<PixelBackground>
    {
        public static readonly PixelBackground Transparent = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground() : this(Colors.Transparent)
        {
        }

        /// <summary>
        ///     A background which is a slice of a kitty image (drawn by the terminal below any
        ///     foreground glyph), with <paramref name="color" /> beneath it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground(Color color, KittyTile tile) : this(color)
        {
            Tile = tile;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground Shade()
        {
            // the tile survives: the terminal cannot tint an image, so the shade shows only where
            // the color background is visible
            return new PixelBackground(Color.Shade(), Tile);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground Brighten()
        {
            return new PixelBackground(Color.Brighten(), Tile);
        }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly Color Color = color;

        /// <summary>The image slice this background shows, or <see cref="KittyTile.None" />.</summary>
        [JsonIgnore] public readonly KittyTile Tile;
#pragma warning restore CA1051 // Do not declare visible instance fields

        public bool Equals(PixelBackground other)
        {
            return Color.Equals(other.Color) && Tile.Equals(other.Tile);
        }

        public override bool Equals([NotNullWhen(true)] object obj)
        {
            return obj is PixelBackground other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Color, Tile);
        }

        public static bool operator ==(PixelBackground left, PixelBackground right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PixelBackground left, PixelBackground right)
        {
            return !left.Equals(right);
        }
    }
}
