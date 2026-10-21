using System.Runtime.InteropServices;
using WheelWizard.Recomp;

namespace WheelWizard.Test.Features.Recomp;

public class RecompPlatformTests
{
    [Theory]
    [InlineData("v1.2/../../../release", "v1-2----------release")]
    [InlineData("", "")]
    [InlineData("v123", "v123")]
    public void CachedSetupNameKeepsTagsWithinSingleFileName(string tag, string sanitized)
    {
        var result = RecompPlatform.CachedSetupFileName(tag);
        Assert.Equal($"WiiCompiled-Setup-{sanitized}{Path.GetExtension(RecompPlatform.SetupFileName)}", result);
        Assert.Equal(result, Path.GetFileName(result));
        Assert.Equal($"WiiCompiled-Setup-*{Path.GetExtension(result)}", RecompPlatform.CachedSetupSearchPattern);
    }

    [Theory]
    [InlineData(Architecture.X64, "WiiCompiled-Setup-x86_64.AppImage")]
    [InlineData(Architecture.Arm64, "WiiCompiled-Setup-aarch64.AppImage")]
    [InlineData(Architecture.X86, null)]
    [InlineData(Architecture.Arm, null)]
    public void LinuxAssetsMatchSupportedArchitectures(Architecture architecture, string? expected) =>
        Assert.Equal(expected, RecompPlatform.LinuxReleaseAssetName(architecture));
}
