using System.Text.Json.Serialization;

namespace WheelWizard.Features.Patches;

public sealed class GameBaselineIndex
{
    [JsonPropertyName("entriesByName")]
    public Dictionary<string, BaselineCandidate[]> EntriesByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BaselineCandidate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string? Region { get; set; }
}

public sealed class GameBaselineData
{
    [JsonPropertyName("entries")]
    public Dictionary<string, BaselineEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BaselineEntry
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("archiveTag")]
    public string ArchiveTag { get; set; } = string.Empty;

    [JsonPropertyName("wholeFileSize")]
    public int WholeFileSize { get; set; }

    [JsonPropertyName("wholeFileHash")]
    public string WholeFileHash { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<object[]>? Members { get; set; }

    [JsonPropertyName("entries")]
    public List<object[]>? Entries { get; set; }
}

public sealed record BaselineMember(int Size, string Hash);

public sealed record BaselineBrsarEntry(string Kind, string? Magic, int Size, string Hash);

public sealed record PatchConversionEntry(string ExportPath, string LogicalPath, byte[] Bytes, string Detail);

public sealed class PatchConversionAnalysis
{
    public string CleanName { get; init; } = string.Empty;
    public string ModdedName { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string? ArchiveTag { get; init; }
    public IReadOnlyList<PatchConversionEntry> Entries { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Skipped { get; init; } = [];
}

public sealed class ModPatchConversionResult
{
    public int ConvertedFileCount { get; init; }
    public int WrittenPatchCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Skipped { get; init; } = [];
}
