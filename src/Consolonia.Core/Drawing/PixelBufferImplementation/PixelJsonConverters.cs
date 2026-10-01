using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    internal static class PixelJsonConverters
    {
        public static T Read<T>(ref Utf8JsonReader reader, JsonSerializerOptions options, JsonConverter<T> fallback)
        {
            return (GetConfiguredConverter<T>(options) ?? fallback).Read(ref reader, typeof(T), options);
        }

        public static void Write<T>(Utf8JsonWriter writer, T value, JsonSerializerOptions options,
            JsonConverter<T> fallback)
        {
            (GetConfiguredConverter<T>(options) ?? fallback).Write(writer, value, options);
        }

        public static JsonConverter<T> GetConfiguredConverter<T>(JsonSerializerOptions options)
        {
            foreach (JsonConverter converter in options.Converters)
            {
                if (!converter.CanConvert(typeof(T)))
                    continue;

                JsonConverter resolved = converter is JsonConverterFactory factory
                    ? factory.CreateConverter(typeof(T), options)
                    : converter;
                return resolved as JsonConverter<T>
                       ?? throw new InvalidOperationException(
                           $"The JSON converter for {typeof(T)} has the wrong type.");
            }

            return null;
        }
    }
}