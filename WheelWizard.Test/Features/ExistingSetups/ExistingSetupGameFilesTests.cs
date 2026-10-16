using System.Text.Json;
using WheelWizard.GameFiles;
using WheelWizard.GameFiles.Views;

namespace WheelWizard.Test.Features.ExistingSetups;

/// <summary>
/// Earlier releases handed Dolphin the game image itself. This release plays an extracted copy, so somebody
/// updating is asked to extract once. Whatever state their setup is in, starting up must not touch their image,
/// rewrite where it is, or remove anything that is theirs.
/// </summary>
public class ExistingSetupGameFilesTests
{
    public static TheoryData<HostPlatform> Platforms => new() { HostPlatform.Windows, HostPlatform.Linux, HostPlatform.MacOS };

    private static ExistingInstall Upgrading(HostPlatform platform)
    {
        var install = platform switch
        {
            HostPlatform.Windows => new ExistingInstall(platform)
                .WithDolphin(@"C:\Dolphin\Dolphin.exe", @"C:\Users\racer\Documents\Dolphin Emulator")
                .WithRealGame(@"D:\Wii Games\Mario Kart Wii (Europe).rvz"),
            HostPlatform.MacOS => new ExistingInstall(platform)
                .WithDolphin("'/Applications/Dolphin.app/Contents/MacOS/Dolphin'", "/Users/racer/Library/Application Support/Dolphin")
                .WithRealGame("/Users/racer/Games/Mario Kart Wii.rvz"),
            _ => new ExistingInstall(platform)
                .WithDolphin("dolphin-emu", "/home/racer/.local/share/dolphin-emu")
                .WithRealGame("/home/racer/Games/Mario Kart Wii.rvz"),
        };
        install.FlushConfig();
        return install;
    }

    private static byte[] Image(ExistingInstall install) => install.Fs.File.ReadAllBytes(install.GameFile);

    private static void AssertStoredGameLocation(ExistingInstall install)
    {
        using var config = JsonDocument.Parse(install.Fs.File.ReadAllText(install.ConfigFilePath));
        Assert.Equal(install.GameFile, config.RootElement.GetProperty("GameLocation").GetString());
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void AnUpgradingUser_IsAskedToExtract_WithEverythingLeftAsItWas(HostPlatform platform)
    {
        var install = Upgrading(platform);
        var image = Image(install);

        var session = install.Boot();

        var status = session.GameFiles.GetStatus();
        Assert.Equal(GameFilesState.NeedsExtraction, status.State);
        Assert.True(GameSetupPage.IsNeeded(status));
        Assert.False(session.Onboarding.IsRequired);
        Assert.Equal(image, Image(install));
        Assert.Equal(install.GameFile, session.Settings.GAME_LOCATION.Get());
        Assert.False(install.Fs.Directory.Exists(session.GamePaths.RootPath), "starting up must not extract by itself");
        AssertStoredGameLocation(install);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void AnUpgradingUserWithTheImageUnplugged_IsAskedToFindIt_AndKeepsTheSetting(HostPlatform platform)
    {
        var install = Upgrading(platform);
        install.Unplug(install.GameFile);

        var session = install.Boot();

        Assert.Equal(GameFilesState.ImageMissing, session.GameFiles.GetStatus().State);
        Assert.Equal(install.GameFile, session.GameFiles.ImagePath);
        Assert.False(session.Onboarding.IsRequired);
        // The stored game location survives the drive being away.
        AssertStoredGameLocation(install);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task AfterExtracting_DolphinIsHandedTheExtractedGame(HostPlatform platform)
    {
        var install = Upgrading(platform);
        var image = Image(install);
        var session = install.Boot();

        var result = await session.GameFiles.ExtractAsync(install.GameFile, replaceExisting: false);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var next = install.Boot();
        Assert.True(next.GameFiles.GetStatus().IsReady);
        next.LaunchDescriptor.GenerateLaunchJson();
        using var json = JsonDocument.Parse(install.Fs.File.ReadAllText(next.Paths.LaunchJsonFilePath));
        Assert.Equal(install.Fs.Path.GetFullPath(next.GamePaths.MainDolPath), json.RootElement.GetProperty("base-file").GetString());
        Assert.Equal(image, Image(install));
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task OnceExtracted_TheImageMayGo(HostPlatform platform)
    {
        var install = Upgrading(platform);
        var session = install.Boot();
        Assert.True((await session.GameFiles.ExtractAsync(install.GameFile, false)).IsSuccess);

        install.Unplug(install.GameFile);
        var next = install.Boot();

        Assert.True(next.GameFiles.GetStatus().IsReady);
        Assert.False(GameSetupPage.IsNeeded(next.GameFiles.GetStatus()));
        Assert.True(next.Settings.DolphinPathsSetupCorrectly());
        Assert.False(next.Onboarding.IsRequired);
    }

    [Fact]
    public void AStartAfterACrashMidExtraction_RemovesOnlyWheelWizardsOwnLeftovers()
    {
        var install = Upgrading(HostPlatform.Windows);
        var session = install.Boot(runStartup: false);
        var staging = session.GamePaths.StagingPath;
        install.Fs.Directory.CreateDirectory(install.Combine(staging, "DATA"));
        install.Fs.File.WriteAllText(install.Combine(staging, GameFilesPaths.WorkMarkerFileName), "");
        var backup = session.GamePaths.RootPath + ".backup-20260101-120000";
        install.Fs.Directory.CreateDirectory(backup);
        install.Fs.File.WriteAllText(install.Combine(backup, "mine.txt"), "mine");

        install.Boot();

        Assert.False(install.Fs.Directory.Exists(staging));
        Assert.Equal("mine", install.Fs.File.ReadAllText(install.Combine(backup, "mine.txt")));
        Assert.True(install.Fs.File.Exists(install.GameFile));
    }

    [Fact]
    public async Task MovingTheDataFolder_TakesTheExtractedGameAlong()
    {
        var install = Upgrading(HostPlatform.Linux);
        var session = install.Boot();
        Assert.True((await session.GameFiles.ExtractAsync(install.GameFile, false)).IsSuccess);

        install.RelocateDataFolder("/mnt/big/WheelWizard");
        var next = install.Boot();

        Assert.True(next.GameFiles.GetStatus().IsReady);
        Assert.StartsWith("/mnt/big/WheelWizard", next.GamePaths.MainDolPath);
    }

    [Fact]
    public void ASetupThatNeverHadAGame_IsNotStoppedByTheGameSetupPage()
    {
        var install = new ExistingInstall(HostPlatform.Windows).WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"C:\Users\racer\Documents\Dolphin Emulator"
        );
        install.FlushConfig();

        var session = install.Boot();

        Assert.False(GameSetupPage.IsNeeded(session.GameFiles.GetStatus()));
    }
}
