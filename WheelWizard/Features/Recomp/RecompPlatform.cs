using System.Runtime.InteropServices;
using WheelWizard.Helpers;

namespace WheelWizard.Recomp;

/// <summary>
/// Which platforms the WiiCompiled integration runs on, and which setup asset each of them downloads.
/// Windows drives <c>WiiCompiled-Setup.exe</c> through the v1 command-line contract. Linux drives the
/// AppImage through its own subcommand interface (see <see cref="RecompLinuxInstallService"/>). Every
/// other platform is unsupported, so <c>ISettingsManager.IsRecompModeActive()</c> is false there and
/// nothing recomp-related is ever registered or shown.
/// </summary>
public static class RecompPlatform
{
    /// <summary>
    /// Whether this is a Linux build that can run the AppImage. A Flatpak sandbox cannot: it has no FUSE,
    /// no compiler prerequisites, and no view of the XDG data directory the AppImage installs into. That
    /// needs a host-spawn and a manifest change, so the option is shown disabled there for now.
    /// </summary>
    public static bool IsLinux { get; } =
        OperatingSystem.IsLinux()
        && !EnvHelper.IsFlatpakSandboxed()
        && LinuxReleaseAssetName(RuntimeInformation.OSArchitecture) is not null;

    /// <summary>A Linux build running inside a Flatpak sandbox: the one Linux where the option exists but cannot be turned on.</summary>
    public static bool IsLinuxFlatpak { get; } = OperatingSystem.IsLinux() && EnvHelper.IsFlatpakSandboxed();

    public static bool IsSupported => OperatingSystem.IsWindows() || IsLinux;

    /// <summary>The name of the installed host copy inside Wheel Wizard's recomp install directory.</summary>
    public static string SetupFileName => IsLinux ? "WiiCompiled-Setup.AppImage" : RecompSetupCommandBuilder.SetupFileName;

    /// <summary>The release asset to download on this machine.</summary>
    public static string ReleaseAssetName =>
        IsLinux ? LinuxReleaseAssetName(RuntimeInformation.OSArchitecture)! : RecompSetupCommandBuilder.SetupFileName;

    /// <summary>
    /// The AppImage the recomp publishes for a Linux architecture, or <see langword="null"/> when it
    /// publishes none. The names are fixed by the recomp's packaging workflow.
    /// </summary>
    public static string? LinuxReleaseAssetName(Architecture architecture) =>
        architecture switch
        {
            Architecture.X64 => "WiiCompiled-Setup-x86_64.AppImage",
            Architecture.Arm64 => "WiiCompiled-Setup-aarch64.AppImage",
            _ => null,
        };

    /// <summary>The file name a downloaded release is cached under; the tag keeps releases apart.</summary>
    public static string CachedSetupFileName(string tagName)
    {
        var sanitized = new string(tagName.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        return $"WiiCompiled-Setup-{sanitized}{Path.GetExtension(SetupFileName)}";
    }

    /// <summary>The pattern that matches every cached setup of this platform, for pruning superseded ones.</summary>
    public static string CachedSetupSearchPattern => $"WiiCompiled-Setup-*{Path.GetExtension(SetupFileName)}";
}
