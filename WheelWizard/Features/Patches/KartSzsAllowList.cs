using System.Reflection;
using System.Text.Json;

namespace WheelWizard.Features.Patches;

public static class KartSzsAllowList
{
    private static readonly Lazy<HashSet<string>> s_allowedNames = new(LoadAllowedNames);

    public static bool IsAllowedFullCharacterOrKart(string fileName) => s_allowedNames.Value.Contains(fileName);

    private static HashSet<string> LoadAllowedNames()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName =
            assembly.GetManifestResourceNames().First(name => name.EndsWith("allowed-kart-szs.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("Embedded kart SZS allow-list was not found.");
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException("Embedded kart SZS allow-list was not found.");
        var names = JsonSerializer.Deserialize<string[]>(stream) ?? [];
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
}
