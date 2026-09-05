using System.Runtime.InteropServices;

namespace WheelWizard.Recomp;

public static class RecompPlatform
{
    public static bool IsSupported =>
        OperatingSystem.IsWindows()
        || (OperatingSystem.IsMacOSVersionAtLeast(14) && RuntimeInformation.ProcessArchitecture == Architecture.Arm64);
    public static string SetupFileName => OperatingSystem.IsMacOS() ? "WiiCompiled-Setup.run" : "WiiCompiled-Setup.exe";
    public static string ReleaseAssetName => OperatingSystem.IsMacOS() ? "WiiCompiled-Setup-macos-arm64.run" : "WiiCompiled-Setup.exe";
    public static string RepositoryOwner => OperatingSystem.IsMacOS() ? "DarthMDev" : RecompReleaseResolver.RepositoryOwner;
    public static string BundledSetupPath =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
                "..",
                "Resources",
                "WiiCompiled-Setup-macos-arm64.run"
            )
        );
}
