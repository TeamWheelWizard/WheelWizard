using System.Text.Json;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// Parses the NDJSON stdout stream of <c>WiiCompiled-Setup.exe --progress-json</c>.
/// Unknown events are ignored, while recognised product reports preserve an explicit invalid state
/// for missing or malformed required fields so callers can fail closed.
/// </summary>
public static class RecompSetupOutputParser
{
    /// <summary>
    /// Parses a single NDJSON line into an event, or returns <see langword="null"/> when the line is not
    /// a recognised event (blank line, diagnostics that leaked into stdout, or a future event type).
    /// </summary>
    public static RecompSetupEvent? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.Trim();
        if (trimmed[0] != '{')
            return null;

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                return null;

            return typeElement.GetString() switch
            {
                "progress" => ParseProgress(root),
                "result" => ParseResult(root),
                "products" => ParseProducts(root),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the plain-text table the Linux host prints for <c>check-products</c>: one
    /// <c>profile  status  installDir</c> row per product, or <c>Nothing installed.</c>. That table
    /// carries no setup version, so the caller supplies the one it recorded at install time.
    /// </summary>
    public static RecompProductsEvent ParseProductsText(IEnumerable<string> lines, string? setupVersion)
    {
        var absent = new RecompProductStatus(RecompProductState.Absent, "not installed");
        var @base = absent;
        var retroRewind = absent;
        string? installDir = null;

        foreach (var line in lines)
        {
            var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2 || columns[0] is not ("base" or "retro-rewind"))
                continue;

            var state = columns[1] switch
            {
                "current" => RecompProductState.Current,
                "MISSING" => RecompProductState.Absent,
                "STALE" => RecompProductState.CompileInputsChanged,
                _ => RecompProductState.Unknown,
            };

            // The row is "{profile,-14} {status,-45} {installDir}", so the directory starts at column 61.
            var hasFixedColumns = line.Length > PlainInstallDirColumn;
            var status = new RecompProductStatus(state, hasFixedColumns ? line[15..PlainInstallDirColumn].Trim() : columns[1]);
            if (columns[0] == "base")
            {
                @base = status;
                continue;
            }

            retroRewind = status;
            installDir = hasFixedColumns ? line[PlainInstallDirColumn..].Trim() : columns[^1];
        }

        return new(setupVersion, installDir, RebuildRequired: false, @base, retroRewind);
    }

    private const int PlainInstallDirColumn = 61;

    private static RecompSetupProgressEvent ParseProgress(JsonElement root) =>
        new(GetString(root, "stage") ?? string.Empty, GetString(root, "message") ?? string.Empty, GetPercent(root, "percent"));

    private static RecompSetupResultEvent ParseResult(JsonElement root)
    {
        var success = root.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
        return new(success, GetString(root, "version"), GetString(root, "installDir"), GetString(root, "error"));
    }

    private static RecompProductsEvent ParseProducts(JsonElement root)
    {
        var setupVersionValid = TryGetRequiredString(root, "setupVersion", out var setupVersion);
        var installDirValid = TryGetRequiredString(root, "installDir", out var installDir);
        var rebuildRequiredValid = TryGetRequiredBoolean(root, "rebuildRequired", out var rebuildRequired);
        var @base = ParseProductStatus(root, "base");
        var retroRewind = ParseProductStatus(root, "retroRewind");
        return new(
            setupVersion,
            installDir,
            rebuildRequired,
            @base,
            retroRewind,
            setupVersionValid && installDirValid && rebuildRequiredValid && @base.ProtocolValid && retroRewind.ProtocolValid
        );
    }

    /// <summary>
    /// A v1 product report is exactly <c>{"status":"...","detail":"..."}</c>. An unrecognised status
    /// stays <see cref="RecompProductState.Unknown"/>, which is the fail-closed answer.
    /// </summary>
    private static RecompProductStatus ParseProductStatus(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
            return RecompProductStatus.Unknown;

        var statusValid = TryGetRequiredString(element, "status", out var status);
        var detailValid = TryGetRequiredString(element, "detail", out var detail);
        return new RecompProductStatus(RecompProductStatus.ParseState(status), detail ?? string.Empty, statusValid && detailValid);
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static bool TryGetRequiredBoolean(JsonElement root, string propertyName, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out var element))
            return false;

        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString();
        return value is not null;
    }

    private static int GetPercent(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Number)
            return 0;

        if (!element.TryGetDouble(out var value) || double.IsNaN(value))
            return 0;

        return Math.Clamp((int)Math.Round(value), 0, 100);
    }
}
