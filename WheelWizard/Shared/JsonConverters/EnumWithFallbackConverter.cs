using System.Text.Json;
using System.Text.Json.Serialization;

namespace WheelWizard.Shared.JsonConverters;

public class EnumWithFallbackConverter<T>(T fallback) : JsonConverter<T> where T : struct, Enum 
{
    public EnumWithFallbackConverter() : this(Enum.GetValues<T>().First()) { }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) 
            return fallback;
        
        var enumString = reader.GetString();
        if (Enum.TryParse(typeof(T), enumString, true, out var result))
            return (T)result;
        
        return fallback;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
