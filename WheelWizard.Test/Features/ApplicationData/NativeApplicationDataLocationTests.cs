using WheelWizard.ApplicationData;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.ApplicationData;

public class NativeApplicationDataLocationTests
{
    [Fact]
    public void TryMove_ReturnsFalse_WhenTargetDriveIsUnavailable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var unavailablePath = GetUnavailableWindowsPath();
        var fs = new Testably.Abstractions.RealFileSystem();
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns(Path.GetTempPath());
        var location = new ApplicationDataLocation(
            fs,
            new ApplicationDataDirectories(fs, environment),
            Substitute.For<IApplicationDataLocationStore>(),
            new DirectoryTransferService(fs)
        );
        var result = location.TryMove(unavailablePath, out var errorMessage, out _);

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
