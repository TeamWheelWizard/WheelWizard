using System.Reflection;
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

    [Fact]
    public void TrySetWheelWizardAppdataPath_ReportsDirectoryCreationFailureBeforeMovingData()
    {
        var temp = Directory.CreateTempSubdirectory("wheelwizard-path-test-");
        var blocker = Path.Combine(temp.FullName, "file");
        File.WriteAllText(blocker, "blocker");
        try
        {
            var result = PathManager.TrySetWheelWizardAppdataPath(Path.Combine(blocker, "data"), out var error, out var move);

            Assert.False(result);
            Assert.StartsWith("Unable to create the selected folder:", error);
            Assert.False(move.CopyAttempted);
            Assert.Equal("blocker", File.ReadAllText(blocker));
        }
        finally
        {
            File.Delete(blocker);
            temp.Delete();
        }
    }

    [Fact]
    public void AccessibilityCheck_ReturnsFalse_WhenDirectoryCreationReturnsFailure()
    {
        var temp = Directory.CreateTempSubdirectory("wheelwizard-path-test-");
        var blocker = Path.Combine(temp.FullName, "file");
        File.WriteAllText(blocker, "blocker");
        try
        {
            var method = typeof(PathManager).GetMethod(
                "TryEnsureWheelWizardAppdataPathAccessible",
                BindingFlags.Static | BindingFlags.NonPublic
            )!;

            Assert.False(Assert.IsType<bool>(method.Invoke(null, [Path.Combine(blocker, "data")])));
        }
        finally
        {
            File.Delete(blocker);
            temp.Delete();
        }
    }

    [Theory]
    [InlineData("child")]
    [InlineData("..cache")]
    public void AppdataValidation_RejectsNestedFolders(string folderName)
    {
        var target = Path.Combine(PathManager.WheelWizardAppdataPath, folderName);

        Assert.False(PathManager.TryValidateWheelWizardAppdataTarget(target, out _, out _, out var error, out _));
        Assert.Contains("inside the current", error);
    }

    private static string GetUnavailableWindowsPath()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var drive = "ZYXWVUTSRQPONMLKJIHGFEDCBA".FirstOrDefault(letter => !used.Contains(letter), 'Z');
        return $@"{drive}:\WheelWizardTests\{Guid.NewGuid():N}";
    }
}
