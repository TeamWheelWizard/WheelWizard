using System.Text.Json;

namespace WheelWizard.Features.Patches;

internal static class PatchConversionHelpers
{
    public static bool TryGetJsonElement(object? value, out JsonElement element)
    {
        if (value is JsonElement jsonElement)
        {
            element = jsonElement;
            return true;
        }

        element = default;
        return false;
    }

    public static string? ReadJsonString(object? value)
    {
        if (!TryGetJsonElement(value, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;

        return element.GetString();
    }

    public static string HashBytes64(byte[] bytes)
    {
        unchecked
        {
            uint hashLow = 0x811c9dc5;
            uint hashHigh = 0x9e3779b1;

            foreach (var value in bytes)
            {
                hashLow = (hashLow ^ value) * 0x01000193;
                hashHigh = (hashHigh ^ value) * 0x85ebca6b;
            }

            return $"{hashLow:x8}{hashHigh:x8}";
        }
    }
}
