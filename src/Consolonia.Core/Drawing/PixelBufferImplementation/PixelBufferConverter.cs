using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    public class PixelBufferConverter : JsonConverter<PixelBuffer>
    {
        private const string PixelsPropertyName = "Pixels";
        private static readonly PixelConverter PixelConverter = new();

        public override PixelBuffer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            ushort width = 0, height = 0;
            Pixel[][] pixels = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName)
                    {
                        case nameof(PixelBuffer.Width):
                            width = reader.GetUInt16();
                            break;
                        case nameof(PixelBuffer.Height):
                            height = reader.GetUInt16();
                            break;
                        case PixelsPropertyName:
                            if (reader.TokenType != JsonTokenType.StartArray)
                                throw new JsonException();

                            var pixelList = new List<Pixel>();
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                var pixel = PixelJsonConverters.Read(ref reader, options, PixelConverter);
                                pixelList.Add(pixel);
                            }

                            pixels = new[] { pixelList.ToArray() };
                            break;
                    }
                }
            }

            if (width == 0 || height == 0 || pixels == null)
                throw new JsonException();

            var pixelBuffer = new PixelBuffer(width, height);
            int i = 0;
            for (ushort y = 0; y < height; y++)
            for (ushort x = 0; x < width; x++)
                pixelBuffer[x, y] = pixels[0][i++];

            return pixelBuffer;
        }

        public override void Write(Utf8JsonWriter writer, PixelBuffer value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(PixelBuffer.Width), value.Width);
            writer.WriteNumber(nameof(PixelBuffer.Height), value.Height);
            writer.WritePropertyName(PixelsPropertyName);
            writer.WriteStartArray();
            for (ushort y = 0; y < value.Height; y++)
            for (ushort x = 0; x < value.Width; x++)
                PixelJsonConverters.Write(writer, value[x, y], options, PixelConverter);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}