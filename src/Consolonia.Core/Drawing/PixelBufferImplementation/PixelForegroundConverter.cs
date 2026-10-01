using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    public class PixelForegroundConverter : JsonConverter<PixelForeground>
    {
        private static readonly SymbolConverter SymbolConverter = new();

        public override PixelForeground Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            var symbol = Symbol.Empty;
            Color color = Colors.Transparent;
            FontWeight? weight = null;
            FontStyle? style = null;
            TextDecorationLocation? textDecoration = null;

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
                        case nameof(PixelForeground.Symbol):
                            symbol = PixelJsonConverters.Read(ref reader, options, SymbolConverter);
                            break;
                        case nameof(PixelForeground.Color):
                            string colorStr = reader.GetString();
                            if (colorStr != null && Color.TryParse(colorStr, out Color parsedColor))
                                color = parsedColor;
                            break;
                        case nameof(PixelForeground.Weight):
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                string weightStr = reader.GetString();
                                if (Enum.TryParse(weightStr, out FontWeight parsedWeight))
                                    weight = parsedWeight;
                            }

                            break;
                        case nameof(PixelForeground.Style):
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                string styleStr = reader.GetString();
                                if (Enum.TryParse(styleStr, out FontStyle parsedStyle))
                                    style = parsedStyle;
                            }

                            break;
                        case nameof(PixelForeground.TextDecoration):
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                string decorationStr = reader.GetString();
                                if (Enum.TryParse(decorationStr, out TextDecorationLocation parsedDecoration))
                                    textDecoration = parsedDecoration;
                            }

                            break;
                    }
                }
            }

            return new PixelForeground(symbol, color, weight, style, textDecoration);
        }

        public override void Write(Utf8JsonWriter writer, PixelForeground value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(nameof(PixelForeground.Symbol));
            PixelJsonConverters.Write(writer, value.Symbol, options, SymbolConverter);

            writer.WriteString(nameof(PixelForeground.Color), value.Color.ToString());

            if (value.Weight.HasValue)
                writer.WriteString(nameof(PixelForeground.Weight), value.Weight.Value.ToString());

            if (value.Style.HasValue)
                writer.WriteString(nameof(PixelForeground.Style), value.Style.Value.ToString());

            if (value.TextDecoration.HasValue)
                writer.WriteString(nameof(PixelForeground.TextDecoration), value.TextDecoration.Value.ToString());

            writer.WriteEndObject();
        }
    }
}