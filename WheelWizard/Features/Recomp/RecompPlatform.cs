using System.Runtime.InteropServices;
using WheelWizard.Helpers;

namespace WheelWizard.Recomp;

public enum RecompHostPlatform
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>
/// Which recomp setup host this machine runs and the GitHub release asset that ships it.
/// The Windows host is a flag-driven setup executable; the Linux host is an AppImage that uses
/// subcommands, keeps its state per user and is cancelled with SIGTERM.
/// </summary>
public static class RecompPlatform
{
    public static RecompHostPlatform Current =>
        OperatingSystem.IsWindows() ? RecompHostPlatform.Windows
        : OperatingSystem.IsMacOS() ? RecompHostPlatform.MacOS
        : RecompHostPlatform.Linux;

    /// <summary>
    /// macOS stays off until WiiCompiled ships a setup host that speaks the CLI contract: its
    /// Setup.pkg is an interactive installer. The Flatpak sandbox cannot run the AppImage host.
    /// </summary>
    public static bool IsSupported =>
        Current switch
        {
            RecompHostPlatform.Windows => true,
            RecompHostPlatform.Linux => !EnvHelper.IsFlatpakSandboxed(),
            _ => false,
        };

    public static string SetupFileName => GetSetupFileName(Current, RuntimeInformation.ProcessArchitecture);

    public static string SetupFileExtension => Path.GetExtension(SetupFileName);

    public static string GetSetupFileName(RecompHostPlatform platform, Architecture architecture) =>
        platform switch
        {
            RecompHostPlatform.Windows => "WiiCompiled-Setup.exe",
            RecompHostPlatform.MacOS => "WiiCompiled-Setup.pkg",
            _ => architecture == Architecture.Arm64 ? "WiiCompiled-Setup-aarch64.AppImage" : "WiiCompiled-Setup-x86_64.AppImage",
        };
}
