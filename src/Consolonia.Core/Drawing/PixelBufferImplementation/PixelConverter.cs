using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Consolonia.Controls;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    public class PixelConverter : JsonConverter<Pixel>
    {
        private static readonly PixelForegroundConverter ForegroundConverter = new();
        private static readonly PixelBackgroundConverter BackgroundConverter = new();

        public override Pixel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            var foreground = PixelForeground.Default;
            PixelBackground background = PixelBackground.Transparent;
            var caretStyle = CaretStyle.None;

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
                        case nameof(Pixel.Foreground):
                            foreground = PixelJsonConverters.Read(ref reader, options, ForegroundConverter);
                            break;
                        case nameof(Pixel.Background):
                            background = PixelJsonConverters.Read(ref reader, options, BackgroundConverter);
                            break;
                        case nameof(Pixel.CaretStyle):
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                string caretStr = reader.GetString();
                                if (Enum.TryParse(caretStr, out CaretStyle parsedCaret))
                                    caretStyle = parsedCaret;
                            }
                            else if (reader.TokenType == JsonTokenType.Number)
                            {
                                caretStyle = (CaretStyle)reader.GetInt32();
                            }

                            break;
                    }
                }
            }

            return new Pixel(foreground, background, caretStyle);
        }

        public override void Write(Utf8JsonWriter writer, Pixel value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(Pixel.Foreground));
            PixelJsonConverters.Write(writer, value.Foreground, options, ForegroundConverter);

            writer.WritePropertyName(nameof(Pixel.Background));
            PixelJsonConverters.Write(writer, value.Background, options, BackgroundConverter);

            writer.WritePropertyName(nameof(Pixel.CaretStyle));
            if (PixelJsonConverters.GetConfiguredConverter<CaretStyle>(options) is { } caretConverter)
                caretConverter.Write(writer, value.CaretStyle, options);
            else
                writer.WriteNumberValue((int)value.CaretStyle);

            writer.WriteEndObject();
        }
    }
}