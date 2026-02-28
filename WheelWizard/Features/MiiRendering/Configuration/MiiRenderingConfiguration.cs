namespace WheelWizard.MiiRendering.Configuration;

public sealed class MiiRenderingConfiguration
{
    public const string ResourceFileName = "FFLResHigh.dat";

    /// <summary>
    /// Optional explicit path to the resource file (or directory containing it).
    /// </summary>
    public string? ResourcePath { get; init; }

    /// <summary>
    /// Optional extra directories to probe for the resource file.
    /// </summary>
    public IReadOnlyList<string> AdditionalSearchDirectories { get; init; } = [];

    /// <summary>
    /// Lower bound for sanity-checking resource integrity.
    /// </summary>
    public long MinimumExpectedSizeBytes { get; init; } = 1024 * 1024;

    public static MiiRenderingConfiguration CreateDefault()
    {
        var configuredPath = Environment.GetEnvironmentVariable("WW_FFLRESHIGH_PATH");
        var configuredDirectories = (Environment.GetEnvironmentVariable("WW_FFLRESHIGH_DIRS") ?? string.Empty).Split(
            ';',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );

        return new MiiRenderingConfiguration { ResourcePath = configuredPath, AdditionalSearchDirectories = configuredDirectories };
    }
}
