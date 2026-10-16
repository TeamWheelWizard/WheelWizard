using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Shared.IO;

namespace WheelWizard.Test.Features.CustomDistributions;

public class RetroRewindMigrationServiceTests
{
    private const string Legacy = "/dolphin-user/Load/Riivolution/WheelWizard";
    private const string Managed = "/launcher-data/RetroRewind";

    [Fact]
    public void Migrate_MovesLegacyInstall_WithSavesAndMarksItMoved()
    {
        var fixture = new Fixture();
        fixture.Fs.Directory.CreateDirectory($"{Legacy}/RetroRewind6");
        fixture.Fs.File.WriteAllText($"{Legacy}/RetroRewind6/version.txt", "6.5.0");
        fixture.Fs.Directory.CreateDirectory($"{Legacy}/riivolution/save/RetroWFC/RMCP");
        fixture.Fs.File.WriteAllText($"{Legacy}/riivolution/save/RetroWFC/RMCP/rksys.dat", "save");
        Assert.True(fixture.Service.IsPending);

        var result = fixture.Service.Migrate();

        Assert.Equal(RetroRewindMigrationOutcome.Moved, result.Value);
        Assert.Equal("save", fixture.Fs.File.ReadAllText($"{Managed}/riivolution/save/RetroWFC/RMCP/rksys.dat"));
        Assert.True(fixture.Fs.File.Exists(fixture.Paths.MigratedMarkerFilePath));
        Assert.False(fixture.Fs.Directory.Exists(Legacy));
        Assert.Equal(Managed, fixture.Paths.RootFolderPath);
        Assert.False(fixture.Service.IsPending);
    }

    [Fact]
    public void Migrate_KeepsAnExistingManagedInstall_AsBackup()
    {
        var fixture = new Fixture();
        fixture.Fs.Directory.CreateDirectory($"{Legacy}/RetroRewind6");
        fixture.Fs.File.WriteAllText($"{Legacy}/RetroRewind6/version.txt", "active");
        fixture.Fs.Directory.CreateDirectory($"{Managed}/RetroRewind6");
        fixture.Fs.File.WriteAllText($"{Managed}/RetroRewind6/version.txt", "older");

        var result = fixture.Service.Migrate();

        Assert.Equal(RetroRewindMigrationOutcome.Moved, result.Value);
        Assert.Equal("active", fixture.Fs.File.ReadAllText($"{Managed}/RetroRewind6/version.txt"));
        var backup = Assert.Single(fixture.Fs.Directory.GetDirectories("/launcher-data", "RetroRewind.old-*"));
        Assert.Equal("older", fixture.Fs.File.ReadAllText(fixture.Fs.Path.Combine(backup, "RetroRewind6", "version.txt")));
    }

    [Fact]
    public void Migrate_IsNotNeeded_WithoutLegacyInstall()
    {
        var fixture = new Fixture();
        fixture.Fs.Directory.CreateDirectory(Legacy);

        var result = fixture.Service.Migrate();

        Assert.Equal(RetroRewindMigrationOutcome.NotNeeded, result.Value);
        Assert.True(fixture.Fs.File.Exists(fixture.Paths.MigratedMarkerFilePath));

        // An old install that shows up later (another Dolphin picked in settings) never takes over.
        fixture.Fs.Directory.CreateDirectory($"{Legacy}/RetroRewind6");
        Assert.False(fixture.Service.IsPending);
        Assert.Equal(Managed, fixture.Paths.RootFolderPath);
    }

    [Fact]
    public void Migrate_IsNotNeeded_WithoutAnyDolphinFolder()
    {
        var fixture = new Fixture(userFolder: "", loadFolder: "");

        var result = fixture.Service.Migrate();

        Assert.Equal(RetroRewindMigrationOutcome.NotNeeded, result.Value);
        Assert.True(fixture.Fs.File.Exists(fixture.Paths.MigratedMarkerFilePath));
    }

    [Fact]
    public void Migrate_WaitsForAnUnreachableDolphinFolder_InsteadOfForgettingTheInstallInIt()
    {
        // The Dolphin folder lives on a drive that is not plugged in during this start.
        var fixture = new Fixture();

        var result = fixture.Service.Migrate();

        Assert.Equal(RetroRewindMigrationOutcome.Deferred, result.Value);
        Assert.False(fixture.Fs.File.Exists(fixture.Paths.MigratedMarkerFilePath));

        // The drive is back on the next start, with the install and its saves.
        fixture.Fs.Directory.CreateDirectory($"{Legacy}/riivolution/save/RetroWFC/RMCP");
        fixture.Fs.File.WriteAllText($"{Legacy}/riivolution/save/RetroWFC/RMCP/rksys.dat", "save");
        Assert.True(fixture.Service.IsPending);

        Assert.Equal(RetroRewindMigrationOutcome.Moved, fixture.Service.Migrate().Value);
        Assert.Equal("save", fixture.Fs.File.ReadAllText($"{Managed}/riivolution/save/RetroWFC/RMCP/rksys.dat"));
    }

    [Fact]
    public void Migrate_WaitsForAMissingCustomLoadFolder_EvenWhenTheUserFolderIsThere()
    {
        var fixture = new Fixture(loadFolder: "/usb/Load");
        fixture.Fs.Directory.CreateDirectory("/dolphin-user/Load");

        Assert.Equal(RetroRewindMigrationOutcome.Deferred, fixture.Service.Migrate().Value);
        Assert.False(fixture.Fs.File.Exists(fixture.Paths.MigratedMarkerFilePath));
    }

    [Fact]
    public void Migrate_CopiesAcrossDrives_ThroughAStagingFolder()
    {
        var fixture = CrossDriveFixture.Create();
        fixture.Fs.Directory.CreateDirectory(@"D:\dolphin-user\Load\Riivolution\WheelWizard\riivolution\save");
        fixture.Fs.File.WriteAllText(@"D:\dolphin-user\Load\Riivolution\WheelWizard\riivolution\save\rksys.dat", "save");
        var copyStarted = false;

        var result = fixture.Service.Migrate(() => copyStarted = true);

        Assert.Equal(RetroRewindMigrationOutcome.Moved, result.Value);
        Assert.True(copyStarted);
        Assert.Equal("save", fixture.Fs.File.ReadAllText(@"C:\launcher-data\RetroRewind\riivolution\save\rksys.dat"));
        Assert.True(fixture.Fs.File.Exists(fixture.Paths.MigratedMarkerFilePath));
        Assert.False(fixture.Fs.Directory.Exists(@"C:\launcher-data\RetroRewind.migrating"));
        Assert.False(fixture.Fs.Directory.Exists(@"D:\dolphin-user\Load\Riivolution\WheelWizard"));
    }

    [Fact]
    public void Migrate_DiscardsACopyThatWasCutOff_AndKeepsNoBackupOfIt()
    {
        var fixture = CrossDriveFixture.Create();
        fixture.Fs.Directory.CreateDirectory(@"D:\dolphin-user\Load\Riivolution\WheelWizard\RetroRewind6");
        fixture.Fs.File.WriteAllText(@"D:\dolphin-user\Load\Riivolution\WheelWizard\RetroRewind6\version.txt", "6.5.0");
        // The app was closed halfway through the copy on an earlier start.
        fixture.Fs.Directory.CreateDirectory(@"C:\launcher-data\RetroRewind.migrating\RetroRewind6");
        Assert.Equal(@"D:\dolphin-user\Load\Riivolution\WheelWizard", fixture.Paths.RootFolderPath);

        var result = fixture.Service.Migrate();

        Assert.Equal(RetroRewindMigrationOutcome.Moved, result.Value);
        Assert.Equal("6.5.0", fixture.Fs.File.ReadAllText(@"C:\launcher-data\RetroRewind\RetroRewind6\version.txt"));
        Assert.Equal([@"C:\launcher-data\RetroRewind"], fixture.Fs.Directory.GetDirectories(@"C:\launcher-data"));
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        public CustomDistributionPaths Paths { get; }
        public RetroRewindMigrationService Service { get; }

        public Fixture(string userFolder = "/dolphin-user", string loadFolder = "")
        {
            var location = Substitute.For<IApplicationDataLocation>();
            location.DirectoryPath.Returns("/launcher-data");
            var dolphin = Substitute.For<IDolphinPaths>();
            dolphin.UserFolderPath.Returns(userFolder);
            dolphin.ConfiguredLoadFolderPath.Returns(loadFolder);
            dolphin.HasCustomLoadFolder.Returns(_ => loadFolder != "" && Fs.Directory.Exists(loadFolder));
            dolphin.LoadFolderPath.Returns(_ => loadFolder != "" && Fs.Directory.Exists(loadFolder) ? loadFolder : $"{userFolder}/Load");
            Paths = new CustomDistributionPaths(location, dolphin, Fs);
            Service = new RetroRewindMigrationService(
                Paths,
                Fs,
                new DirectoryTransferService(Fs),
                System.TimeProvider.System,
                NullLogger<RetroRewindMigrationService>.Instance
            );
        }
    }

    private sealed record CrossDriveFixture(MockFileSystem Fs, CustomDistributionPaths Paths, RetroRewindMigrationService Service)
    {
        public static CrossDriveFixture Create()
        {
            var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Windows)).WithDrive("D:");
            // Windows cannot rename a folder onto another drive.
            fs.Intercept.Event(
                _ => throw new IOException("Source and destination path must have identical roots."),
                change =>
                    change.ChangeType == WatcherChangeTypes.Renamed
                    && change.FileSystemType == FileSystemTypes.Directory
                    && change.OldPath?.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase) == true
            );
            var location = Substitute.For<IApplicationDataLocation>();
            location.DirectoryPath.Returns(@"C:\launcher-data");
            var dolphin = Substitute.For<IDolphinPaths>();
            dolphin.UserFolderPath.Returns(@"D:\dolphin-user");
            dolphin.ConfiguredLoadFolderPath.Returns("");
            dolphin.LoadFolderPath.Returns(@"D:\dolphin-user\Load");
            var paths = new CustomDistributionPaths(location, dolphin, fs);
            var service = new RetroRewindMigrationService(
                paths,
                fs,
                new DirectoryTransferService(fs),
                System.TimeProvider.System,
                NullLogger<RetroRewindMigrationService>.Instance
            );
            return new(fs, paths, service);
        }
    }
}
