using Semver;

namespace WheelWizard.Recomp.Domain;

/// <summary>
/// UI progress update produced while installing or updating the recomp.
/// </summary>
public sealed record RecompInstallProgress(string Message, int Percent);

/// <summary>
/// A GitHub release of the recomp that actually carries the setup executable.
/// </summary>
/// <param name="TagName">The raw release tag (e.g. <c>v0.3.0</c>).</param>
/// <param name="Version">The parsed semantic version of <paramref name="TagName"/>.</param>
/// <param name="SetupDownloadUrl">Direct download URL of the <c>WiiCompiled-Setup.exe</c> asset.</param>
public sealed record RecompRelease(string TagName, SemVersion Version, string SetupDownloadUrl);

/// <summary>
/// The contents of <c>install-state.json</c>, written by the recomp setup executable into its install directory.
/// Its existence (and parse-ability) is what marks the recomp as installed.
/// </summary>
public class RecompInstallState
{
    public int? SchemaVersion { get; set; }
    public string? SetupVersion { get; set; }
    public string? InstallDir { get; set; }

    /// <summary>
    /// How the installed Retro Rewind product was built: <c>downloaded</c> when it embeds a Retro-WFC
    /// payload, <c>skipped</c> when it was deliberately built without one, or empty for a base-only install.
    /// </summary>
    public string? RetroWfcPayloadMode { get; set; }

    /// <summary>Whether the installed Retro Rewind product was built without a Retro-WFC payload.</summary>
    public bool IsRetroWfcPayloadSkipped => string.Equals(RetroWfcPayloadMode, "skipped", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The Retro-WFC payload choice handed to the setup executable. The contract requires exactly one whenever a
/// Retro Rewind source directory is passed.
/// </summary>
public enum RecompRetroWfcPayloadMode
{
    /// <summary>Let the setup host download the shared payload (<c>--download-retro-wfc-payload</c>).</summary>
    Download,

    /// <summary>Build Retro Rewind without the payload (<c>--skip-retro-wfc-payload</c>); online play is unavailable.</summary>
    Skip,
}

/// <summary>
/// Everything WheelWizard hands to the recomp setup executable for a silent install / in-place upgrade.
/// All paths come from WheelWizard's existing configuration; the recomp owns everything else.
/// </summary>
public sealed record RecompInstallRequest
{
    /// <summary>Path to the user's Mario Kart Wii disc image (WheelWizard's <c>GameLocation</c> setting).</summary>
    public required string GameFilePath { get; init; }

    /// <summary>Directory the recomp should install into.</summary>
    public required string InstallFolderPath { get; init; }

    /// <summary>The already-extracted Retro Rewind folder, or <see langword="null"/> to let the recomp decide.</summary>
    public string? RetroRewindFolderPath { get; init; }

    /// <summary>
    /// Whether <see cref="InstallFolderPath"/> is the portable location, i.e. <c>&lt;root&gt;\Install</c>
    /// of a Wheel Wizard-owned portable root. Only a portable target may receive <c>--portable</c>;
    /// an installation that predates portable mode keeps its machine-local layout.
    /// </summary>
    public bool Portable { get; init; }

    /// <summary>
    /// The Retro-WFC payload choice for the Retro Rewind product. Only meaningful together with
    /// <see cref="RetroRewindFolderPath"/>.
    /// </summary>
    public RecompRetroWfcPayloadMode RetroWfcPayloadMode { get; init; } = RecompRetroWfcPayloadMode.Download;
}
