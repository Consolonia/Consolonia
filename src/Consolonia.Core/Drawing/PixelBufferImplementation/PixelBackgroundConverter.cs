using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    public class PixelBackgroundConverter : JsonConverter<PixelBackground>
    {
        public override PixelBackground Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                reader.Read(); // Move to property name
                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.GetString() == nameof(PixelBackground.Color))
                {
                    reader.Read(); // Move to property value
                    string colorValue = reader.GetString();
                    reader.Read(); // Move past end object

                    if (colorValue != null && Color.TryParse(colorValue, out Color color))
                        return new PixelBackground(color);
                }
            }

            return new PixelBackground(Colors.Transparent);
        }

        public override void Write(Utf8JsonWriter writer, PixelBackground value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(PixelBackground.Color), value.Color.ToString());
            writer.WriteEndObject();
        }
    }
}