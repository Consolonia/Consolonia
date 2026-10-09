using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Avalonia;

// ReSharper disable UnusedMember.Global

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    [JsonConverter(typeof(PixelBufferConverter))]
    public class PixelBuffer
    {
        private readonly Pixel[,] _buffer;

        public PixelBuffer(PixelBufferSize size)
            : this(size.Width, size.Height)
        {
        }

        public PixelBuffer(ushort width, ushort height)
        {
            Width = width;
            Height = height;
            _buffer = new Pixel[width, height];

            // initialize the buffer with space so it draws any background color
            // blended into it.
            for (ushort y = 0; y < height; y++)
            for (ushort x = 0; x < width; x++)
                _buffer[x, y] = Pixel.Space;
        }

        // ReSharper disable once UnusedMember.Global
        [JsonIgnore]
        public Pixel this[int i]
        {
            get
            {
                (ushort x, ushort y) = ToXY(i);
                return this[x, y];
            }
            set
            {
                (ushort x, ushort y) = ToXY(i);
                this[x, y] = value;
            }
        }

        [JsonIgnore]
        public Pixel this[PixelBufferCoordinate point]
        {
            get => _buffer[point.X, point.Y];
            // ReSharper disable once MemberCanBePrivate.Global
            set => this[point.X, point.Y] = value;
        }

        [JsonIgnore]
        public Pixel this[ushort x, ushort y]
        {
            get => _buffer[x, y];
            // ReSharper disable once MemberCanBePrivate.Global
            set => _buffer[x, y] = value;
        }

        [JsonIgnore]
        public Pixel this[PixelPoint point]
        {
            get => this[(PixelBufferCoordinate)point];
            set => this[(PixelBufferCoordinate)point] = value;
        }

        /// <summary>The cell at (x, y), read in place rather than copied, for scans that only look.</summary>
        internal ref readonly Pixel CellAt(int x, int y)
        {
            return ref _buffer[x, y];
        }

        [JsonIgnore] public int Length => _buffer.Length;

        [JsonIgnore] public PixelRect Size => new(0, 0, Width, Height);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (ushort x, ushort y) ToXY(int i)
        {
            return ((ushort x, ushort y))(i % Width, i / Width);
        }

        public string PrintBuffer()
        {
            var stringBuilder = new StringBuilder();

            for (ushort j = 0; j < Height; j++)
            {
                for (ushort i = 0; i < Width;)
                {
                    Pixel pixel = this[new PixelBufferCoordinate(i, j)];

                    if (pixel.IsCaret())
                    {
                        stringBuilder.Append('Ꮖ');
                        i += Math.Max(pixel.Width, (ushort)1);
                    }
                    else if (pixel.Width > 0)
                    {
                        stringBuilder.Append(pixel.Foreground.Symbol.GetText());
                        i += pixel.Width;
                    }
                    else
                    {
                        i++;
                    }
                }

                stringBuilder.AppendLine();
            }

            return stringBuilder.ToString();
        }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly ushort Width;
        public readonly ushort Height;
#pragma warning restore CA1051 // Do not declare visible instance fields
    }
}