using WheelWizard.Services;

namespace WheelWizard.Test.Features.Settings;

[Collection("SettingsFeature")]
public class PathManagerTests
{
    [Fact]
    public void TrySetWheelWizardAppdataPath_ReturnsFalse_WhenTargetPathIsUnavailable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var unavailablePath = GetUnavailableWindowsPath();
        var result = PathManager.TrySetWheelWizardAppdataPath(unavailablePath, out var errorMessage, out _);

        Assert.False(result);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    private static string GetUnavailableWindowsPath()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var drive = "ZYXWVUTSRQPONMLKJIHGFEDCBA".FirstOrDefault(letter => !used.Contains(letter), 'Z');
        return $@"{drive}:\WheelWizardTests\{Guid.NewGuid():N}";
    }
}
