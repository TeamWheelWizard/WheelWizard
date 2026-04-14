using WheelWizard.Services;

namespace WheelWizard.MiiRendering.Configuration;

public sealed class MiiRenderingConfiguration
{
    public const string ResourceFileName = "FFLResHigh.dat";

    /// <summary>
    /// The managed Wheel Wizard location for the resource file.
    /// </summary>
    public string ManagedResourcePath { get; init; } = PathManager.MiiRenderingResourceFilePath;

    /// <summary>
    /// Lower bound for sanity-checking resource integrity.
    /// </summary>
    public long MinimumExpectedSizeBytes { get; init; } = 1024 * 1024;

    public static MiiRenderingConfiguration CreateDefault()
    {
        return new MiiRenderingConfiguration { ManagedResourcePath = PathManager.MiiRenderingResourceFilePath };
    }
}
