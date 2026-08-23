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
}
