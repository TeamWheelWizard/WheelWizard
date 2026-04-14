using System.Text.Json;
using System.Text.Json.Serialization;

namespace WheelWizard.GameBanana.Domain;

public class GameBananaTag
{
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class GameBananaTagListJsonConverter : JsonConverter<List<GameBananaTag>>
{
    public override List<GameBananaTag> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected StartArray token, but got {reader.TokenType}.");

        var tags = new List<GameBananaTag>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return tags;

            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var tagText = reader.GetString() ?? string.Empty;
                    tags.Add(new() { Title = tagText, Value = tagText });
                    break;
                case JsonTokenType.StartObject:
                    using (var tagDocument = JsonDocument.ParseValue(ref reader))
                    {
                        var root = tagDocument.RootElement;
                        tags.Add(
                            new()
                            {
                                Title = root.TryGetProperty("_sTitle", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                                Value = root.TryGetProperty("_sValue", out var value) ? value.GetString() ?? string.Empty : string.Empty,
                            }
                        );
                    }
                    break;

                default:
                    using (JsonDocument.ParseValue(ref reader)) { }
                    break;
            }
        }

        throw new JsonException("Unexpected end of GameBanana tag payload.");
    }

    public override void Write(Utf8JsonWriter writer, List<GameBananaTag> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var tag in value)
        {
            writer.WriteStartObject();
            writer.WriteString("_sTitle", tag.Title);
            writer.WriteString("_sValue", tag.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
