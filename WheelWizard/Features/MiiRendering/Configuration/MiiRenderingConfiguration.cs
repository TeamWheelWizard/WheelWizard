namespace WheelWizard.MiiRendering.Configuration;

public sealed class MiiRenderingConfiguration
{
    public const string ResourceFileName = "FFLResHigh.dat";

    /// <summary>Lower bound for sanity-checking resource integrity.</summary>
    public long MinimumExpectedSizeBytes { get; init; } = 1024 * 1024;
}
