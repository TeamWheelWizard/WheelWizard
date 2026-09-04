using System.Runtime.InteropServices;
using WheelWizard.GitHub.Domain;
using WheelWizard.Models.Enums;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Test.Features.Recomp;

/// <summary>
/// A deliberately small smoke suite over the contracts that break silently when the recomp is
/// updated: the release asset we pick, the command line we hand the setup host, the report we read
/// back, and the status that report maps onto. Everything here is string in / value out, so it runs
/// the same everywhere.
/// </summary>
public class RecompTests
{
    [Fact]
    public void SilentInstall_BuildsTheCommandLineTheWindowsSetupExpects()
    {
        var arguments = RecompSetupCommandBuilder.BuildSilentInstallArguments(
            RecompHostPlatform.Windows,
            new()
            {
                GameFilePath = @"D:\Games\Mario Kart Wii.rvz",
                InstallFolderPath = @"D:\WheelWizard\Recomp\Install",
                RetroRewindFolderPath = @"D:\WheelWizard\RetroRewind6",
                Portable = true,
            }
        );

        Assert.Equal(
            "--silent --game \"D:\\Games\\Mario Kart Wii.rvz\" --install-dir \"D:\\WheelWizard\\Recomp\\Install\" --portable "
                + "--progress-json --retro-dir \"D:\\WheelWizard\\RetroRewind6\" --download-retro-wfc-payload",
            arguments
        );
        Assert.Equal("--launch-retro", RecompSetupCommandBuilder.BuildLaunchArguments(RecompHostPlatform.Windows, retroRewind: true));
        Assert.StartsWith(
            "--check-products --install-dir",
            RecompSetupCommandBuilder.BuildCheckProductsArguments(RecompHostPlatform.Windows, @"D:\WheelWizard\Recomp\Install")
        );
    }

    [Fact]
    public void SilentInstall_BuildsTheSubcommandsTheLinuxHostExpects()
    {
        var arguments = RecompSetupCommandBuilder.BuildSilentInstallArguments(
            RecompHostPlatform.Linux,
            new()
            {
                GameFilePath = "/home/user/Games/Mario Kart Wii.rvz",
                InstallFolderPath = "/home/user/.local/share/WheelWizard/Recomp/Install",
                RetroRewindFolderPath = "/home/user/.local/share/WheelWizard/RetroRewind6",
                Portable = false,
            }
        );

        Assert.Equal(
            "install --game \"/home/user/Games/Mario Kart Wii.rvz\" --install-dir \"/home/user/.local/share/WheelWizard/Recomp/Install\" "
                + "--progress-json --retro-dir \"/home/user/.local/share/WheelWizard/RetroRewind6\" --download-retro-wfc-payload",
            arguments
        );
        Assert.Equal("launch-retro", RecompSetupCommandBuilder.BuildLaunchArguments(RecompHostPlatform.Linux, retroRewind: true));
        Assert.Equal("check-products", RecompSetupCommandBuilder.BuildCheckProductsArguments(RecompHostPlatform.Linux, "/install"));
        Assert.Equal("uninstall", RecompSetupCommandBuilder.BuildUninstallArguments());
    }

    [Fact]
    public void ReleaseResolver_PicksTheNewestReleaseThatShipsThePlatformAsset()
    {
        var releases = new List<GithubRelease>
        {
            Release("v0.2.27", "WiiCompiled-Setup.exe"),
            Release("v0.2.26", "WiiCompiled-Setup.exe", "WiiCompiled-Setup-x86_64.AppImage", "WiiCompiled-Setup-aarch64.AppImage"),
            Release("v0.2.25", "WiiCompiled-Setup.exe"),
        };

        var windows = RecompReleaseResolver.FindLatest(
            releases,
            RecompPlatform.GetSetupFileName(RecompHostPlatform.Windows, Architecture.X64)
        );
        Assert.Equal("v0.2.27", windows?.TagName);

        var linux = RecompReleaseResolver.FindLatest(releases, RecompPlatform.GetSetupFileName(RecompHostPlatform.Linux, Architecture.X64));
        Assert.Equal("v0.2.26", linux?.TagName);
        Assert.EndsWith("WiiCompiled-Setup-x86_64.AppImage", linux!.SetupDownloadUrl);

        var linuxArm = RecompReleaseResolver.FindLatest(
            releases,
            RecompPlatform.GetSetupFileName(RecompHostPlatform.Linux, Architecture.Arm64)
        );
        Assert.EndsWith("WiiCompiled-Setup-aarch64.AppImage", linuxArm!.SetupDownloadUrl);

        Assert.Null(
            RecompReleaseResolver.FindLatest(releases, RecompPlatform.GetSetupFileName(RecompHostPlatform.MacOS, Architecture.Arm64))
        );
    }

    [Fact]
    public void ProductsLine_IsReadBackAsTheProductStateItReports()
    {
        var parsed = RecompSetupOutputParser.Parse(
            """
            {"type":"products","setupVersion":"0.3.0","installDir":"D:\\WiiCompiled","rebuildRequired":false,"base":{"status":"current","detail":"ok"},"retroRewind":{"status":"code-pul-changed","detail":"Code.pul changed"}}
            """
        );

        var products = Assert.IsType<RecompProductsEvent>(parsed);
        Assert.Equal("0.3.0", products.SetupVersion);
        Assert.True(products.Base.IsCurrent);
        Assert.Equal(RecompProductState.CodePulChanged, products.RetroRewind.State);
        Assert.True(products.ActionRequired);
    }

    [Fact]
    public void ProductsTable_FromTheLinuxHost_IsReadBackAsTheProductStateItReports()
    {
        var lines = new[]
        {
            $"{"base", -14} {"current", -45} /home/user/.local/share/WiiCompiled/Install/Base",
            $"{"retro-rewind", -14} {"current", -45} /home/user/Wheel Wizard/Recomp/Install",
        };
        var products = RecompSetupOutputParser.ParseProductsText(lines, "0.2.26");

        Assert.Equal("0.2.26", products.SetupVersion);
        Assert.Equal("/home/user/Wheel Wizard/Recomp/Install", products.InstallDir);
        Assert.True(products.Base.IsCurrent);
        Assert.True(products.RetroRewind.IsCurrent);
        Assert.False(products.ActionRequired);

        var stale = RecompSetupOutputParser.ParseProductsText(
            [$"{"retro-rewind", -14} {"STALE (game assets changed since last build)", -45} /home/user/Recomp/Install"],
            "0.2.26"
        );
        Assert.Equal(RecompProductState.CompileInputsChanged, stale.RetroRewind.State);
        Assert.Equal("/home/user/Recomp/Install", stale.InstallDir);
        Assert.True(stale.ActionRequired);

        var nothing = RecompSetupOutputParser.ParseProductsText(["Nothing installed."], "0.2.26");
        Assert.Equal(RecompProductState.Absent, nothing.RetroRewind.State);
        Assert.Null(nothing.InstallDir);
    }

    [Fact]
    public void Status_IsOnlyReadyWhenTheInstallWasActuallyVerified()
    {
        var current = new RecompProductStatus(RecompProductState.Current, "ok");
        var verified = new RecompProductsEvent("0.3.0", @"D:\WiiCompiled", RebuildRequired: false, current, current);

        Assert.Equal(WheelWizardStatus.Ready, RecompStatusResolver.Resolve(true, "0.3.0", "v0.3.0", verified));
        Assert.Equal(WheelWizardStatus.OutOfDate, RecompStatusResolver.Resolve(true, "0.3.0", "v0.4.0", verified));
        Assert.Equal(WheelWizardStatus.NotInstalled, RecompStatusResolver.Resolve(true, null, "v0.3.0", verified));
        Assert.Equal(WheelWizardStatus.ConfigNotFinished, RecompStatusResolver.Resolve(false, "0.3.0", "v0.3.0", verified));

        // A check that never answered must never read as ready.
        Assert.Equal(WheelWizardStatus.OutOfDate, RecompStatusResolver.Resolve(true, "0.3.0", "v0.3.0", products: null));
    }

    private static GithubRelease Release(string tag, params string[] assetNames) =>
        new()
        {
            TagName = tag,
            Assets = assetNames
                .Select(name => new GithubAsset { Name = name, BrowserDownloadUrl = $"https://example.invalid/{tag}/{name}" })
                .ToList(),
        };
}
