using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Infrastructure;

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

        [JsonIgnore] public int Length => _buffer.Length;

        [JsonIgnore] public PixelRect Size => new(0, 0, Width, Height);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (ushort x, ushort y) ToXY(int i)
        {
            return ((ushort x, ushort y))(i % Width, i / Width);
        }

        /// <summary>
        /// Get the pixel to render at the given coordinate, taking into account mouse cursor
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="consoleCursor"></param>
        /// <returns></returns>
        public Pixel GetPixelForRendering(ushort x, ushort y, ConsoleCursor consoleCursor)
        {
            Pixel pixel = this[x, y];

            if (!consoleCursor.IsEmpty() &&
                consoleCursor.Coordinate.Y == y &&
                consoleCursor.Coordinate.X <= x && x < consoleCursor.Coordinate.X + consoleCursor.Width)
            {
                if (consoleCursor.Type == " " && pixel.Width == 1)
                {
                    // floating cursor tracking effect 
                    // if we are drawing a " " and the pixel underneath is not wide char
                    // then we lift the character from the underlying pixel and invert it
                    char cursorChar = pixel.Foreground.Symbol.Character != '\0'
                        ? pixel.Foreground.Symbol.Character
                        : ' ';
                    pixel = new Pixel(new PixelForeground(new Symbol(cursorChar, 1), pixel.Background.Color),
                        new PixelBackground(pixel.Background.Color.GetContrastColor()));
                }
                else
                {
                    char cursorChar = consoleCursor.Type[x - consoleCursor.Coordinate.X];
                    // simply draw the mouse cursor character in the current pixel colors.
                    Color foreground = pixel.Foreground.Color != Colors.Transparent
                        ? pixel.Foreground.Color
                        : pixel.Background.Color.GetContrastColor();
                    pixel = new Pixel(
                        new PixelForeground(new Symbol(cursorChar, 1), foreground,
                            pixel.Foreground.Weight, pixel.Foreground.Style, pixel.Foreground.TextDecoration),
                        pixel.Background, pixel.CaretStyle);
                }
            }

            
            // Handle wide glyphs similarly to ConsoleOutputDeviceRenderer.
            bool isWide = false;
            if (pixel.Width > 1)
            {
                isWide = true;

                // If the wide glyph would overlap non-empty continuation cells, render space instead.
                for (ushort i = 1; i < pixel.Width && x + i < Width; i++)
                {
                    if (this[(ushort)(x + i), y].Width != 0)
                    {
                        pixel = Pixel.Space;
                        break;
                    }
                }
            }

            if (pixel.Width == 0 && !isWide)
                pixel = Pixel.Space;

            return pixel;
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