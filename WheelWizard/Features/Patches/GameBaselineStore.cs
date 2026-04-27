using System.Reflection;
using System.Text.Json;

namespace WheelWizard.Features.Patches;

public sealed class GameBaselineStore
{
    private const string ResourcePrefix = "WheelWizard.Features.Patches.Resources.";
    private static readonly Lazy<GameBaselineStore> s_instance = new(() => new GameBaselineStore());
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static GameBaselineStore Instance => s_instance.Value;

    private readonly Lazy<GameBaselineIndex> _index = new(() => LoadResource<GameBaselineIndex>("game-baseline-index.json"));
    private readonly Lazy<GameBaselineData> _data = new(() => LoadResource<GameBaselineData>("game-baseline-data.json"));

    public IReadOnlyList<BaselineCandidate> FindCandidates(string fileName, string? kind = null)
    {
        if (_index.Value.EntriesByName.TryGetValue(fileName.ToLowerInvariant(), out var candidates))
        {
            return candidates
                .Where(candidate => kind == null || string.Equals(candidate.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return [];
    }

    public BaselineEntry? GetEntry(string id) => _data.Value.Entries.TryGetValue(id, out var entry) ? entry : null;

    private static T LoadResource<T>(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = ResourcePrefix + fileName.Replace("-", "_").Replace(".json", ".json");
        resourceName =
            assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? resourceName;

        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded patches resource '{fileName}' was not found.");
        return JsonSerializer.Deserialize<T>(stream, s_jsonOptions)
            ?? throw new InvalidOperationException($"Embedded patches resource '{fileName}' could not be parsed.");
    }
}
