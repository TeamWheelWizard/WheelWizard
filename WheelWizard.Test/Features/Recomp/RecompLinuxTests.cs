using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.GitHub.Domain;
using WheelWizard.Models.Enums;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Test.Features.Recomp;

/// <summary>
/// The Linux AppImage speaks a different dialect than the Windows setup: subcommands, no install
/// directory of Wheel Wizard's choosing, and no product report on stdout. These tests pin the contracts
/// Wheel Wizard relies on there: the command lines, the release asset, and the product state it
/// reconstructs from the files the AppImage writes.
/// </summary>
public class RecompLinuxTests
{
    [Fact]
    public void Install_PassesTheGameAndRetroRewindTheWayTheAppImageExpects()
    {
        var arguments = RecompLinuxSetupCommandBuilder.BuildInstallArguments(
            "/home/user/Games/Mario Kart Wii.rvz",
            "/home/user/.local/share/WheelWizard/RiivolutionWW/RetroRewind6"
        );

        Assert.Equal(
            "install --game \"/home/user/Games/Mario Kart Wii.rvz\" "
                + "--retro-dir \"/home/user/.local/share/WheelWizard/RiivolutionWW/RetroRewind6\" --download-retro-wfc-payload --progress-json",
            arguments
        );
    }

    [Fact]
    public void Install_WithoutAGameReusesTheExtractedAssets()
    {
        var arguments = RecompLinuxSetupCommandBuilder.BuildInstallArguments(null, "/rr/RetroRewind6", RecompRetroWfcPayloadMode.Skip);

        Assert.Equal("install --retro-dir \"/rr/RetroRewind6\" --skip-retro-wfc-payload --progress-json", arguments);
        Assert.DoesNotContain("--game", arguments);
    }

    [Fact]
    public void Install_BaseOnlyPassesNoPayloadOption()
    {
        Assert.Equal("install --game \"/g.iso\" --progress-json", RecompLinuxSetupCommandBuilder.BuildInstallArguments("/g.iso", null));
    }

    [Theory]
    [InlineData("/plain/path", "\"/plain/path\"")]
    [InlineData("/with \"quotes\"/x", "\"/with \\\"quotes\\\"/x\"")]
    [InlineData("/back\\slash\\", "\"/back\\slash\\\\\"")]
    [InlineData("/odd\\\"mix", "\"/odd\\\\\\\"mix\"")]
    public void Quote_MatchesTheDotnetArgumentParser(string path, string expected)
    {
        Assert.Equal(expected, RecompLinuxSetupCommandBuilder.Quote(path));
    }

    [Fact]
    public void ReleaseAsset_FollowsTheMachineArchitecture()
    {
        Assert.Equal("WiiCompiled-Setup-x86_64.AppImage", RecompPlatform.LinuxReleaseAssetName(Architecture.X64));
        Assert.Equal("WiiCompiled-Setup-aarch64.AppImage", RecompPlatform.LinuxReleaseAssetName(Architecture.Arm64));
        Assert.Null(RecompPlatform.LinuxReleaseAssetName(Architecture.X86));
    }

    [Fact]
    public void ReleaseResolver_PicksTheAssetItIsAskedFor()
    {
        var release = new GithubRelease
        {
            TagName = "v0.2.27",
            Assets =
            [
                new GithubAsset { Name = "WiiCompiled-Setup.exe", BrowserDownloadUrl = "https://example.com/exe" },
                new GithubAsset { Name = "WiiCompiled-Setup-x86_64.AppImage", BrowserDownloadUrl = "https://example.com/x86_64" },
                new GithubAsset { Name = "WiiCompiled-Setup-aarch64.AppImage", BrowserDownloadUrl = "https://example.com/aarch64" },
            ],
        };
        var windowsOnly = new GithubRelease
        {
            TagName = "v0.3.0",
            Assets = [new GithubAsset { Name = "WiiCompiled-Setup.exe", BrowserDownloadUrl = "https://example.com/newer-exe" }],
        };

        var linux = RecompReleaseResolver.FindLatest([release, windowsOnly], "WiiCompiled-Setup-x86_64.AppImage");
        Assert.NotNull(linux);
        Assert.Equal("v0.2.27", linux.TagName);
        Assert.Equal("https://example.com/x86_64", linux.SetupDownloadUrl);

        Assert.Equal("v0.3.0", RecompReleaseResolver.FindLatest([release, windowsOnly], "WiiCompiled-Setup.exe")?.TagName);
        Assert.Null(RecompReleaseResolver.FindLatest([windowsOnly], "WiiCompiled-Setup-aarch64.AppImage"));
    }

    [Fact]
    public void Inspector_ReportsBothProductsAbsentWhenTheAppImageNeverInstalled()
    {
        var fixture = new BackendFixture();

        var products = fixture.Inspect();

        Assert.Equal(RecompProductState.Absent, products.Base.State);
        Assert.Equal(RecompProductState.Absent, products.RetroRewind.State);
        Assert.False(products.ActionRequired);
        Assert.True(products.ProtocolValid);
        Assert.Equal("0.2.27", products.SetupVersion);
    }

    [Fact]
    public void Inspector_ReportsCurrentWhenTheExecutablesExistAndCodePulMatches()
    {
        var fixture = new BackendFixture().WithBase().WithRetroRewind(codePulMatches: true);

        var products = fixture.Inspect();

        Assert.True(products.Base.IsCurrent);
        Assert.True(products.RetroRewind.IsCurrent);
        Assert.Equal(WheelWizardStatus.Ready, RecompStatusResolver.Resolve(true, "0.2.27", "v0.2.27", products));
    }

    [Fact]
    public void Inspector_ReportsCodePulChangedWhenRetroRewindWasUpdated()
    {
        var fixture = new BackendFixture().WithBase().WithRetroRewind(codePulMatches: false);

        var products = fixture.Inspect();

        Assert.True(products.Base.IsCurrent);
        Assert.Equal(RecompProductState.CodePulChanged, products.RetroRewind.State);
        Assert.True(products.RetroRewind.RequiresCompile);
        Assert.True(products.ActionRequired);
        Assert.Equal(WheelWizardStatus.OutOfDate, RecompStatusResolver.Resolve(true, "0.2.27", "v0.2.27", products));
    }

    [Fact]
    public void Inspector_FailsClosedWhenTheBuildRecordIsMissing()
    {
        var fixture = new BackendFixture().WithBase().WithRetroRewind(codePulMatches: true, writeBuildRecord: false);

        Assert.Equal(RecompProductState.CodePulChanged, fixture.Inspect().RetroRewind.State);
    }

    [Fact]
    public void Inspector_ReportsBrokenWhenAnExecutableVanished()
    {
        var fixture = new BackendFixture().WithBase().WithRetroRewind(codePulMatches: true);
        fixture.FileSystem.File.Delete(fixture.FileSystem.Path.Combine(fixture.BaseFolder, "WiiCompiled"));

        var products = fixture.Inspect();

        Assert.Equal(RecompProductState.Broken, products.Base.State);
        Assert.True(products.IsBlocked);
    }

    [Fact]
    public void Inspector_OnlyChecksPresenceWithoutARetroRewindSource()
    {
        var fixture = new BackendFixture().WithBase().WithRetroRewind(codePulMatches: false);

        Assert.True(fixture.Inspect(retroRewindFolder: null).RetroRewind.IsCurrent);
    }

    [Fact]
    public void Inspector_ReadsANullProductListAsNothingBuilt()
    {
        var fixture = new BackendFixture();
        fixture.FileSystem.File.WriteAllText(fixture.StateFile, """{"SchemaVersion":1,"Workspace":"w","Products":null}""");

        var products = fixture.Inspect();

        Assert.True(products.ProtocolValid);
        Assert.Equal(RecompProductState.Absent, products.Base.State);
        Assert.Equal(RecompProductState.Absent, products.RetroRewind.State);
    }

    [Fact]
    public void Inspector_TreatsAMalformedStateAsUnverifiable()
    {
        var fixture = new BackendFixture();
        fixture.FileSystem.File.WriteAllText(fixture.StateFile, "not json");

        var products = fixture.Inspect();

        Assert.False(products.ProtocolValid);
        Assert.True(products.IsBlocked);
    }

    /// <summary>
    /// The files the AppImage leaves behind after an install, laid out the way its install-state.json,
    /// local-build.json and Wheel Wizard's RetroRewind6 folder relate on a real machine.
    /// </summary>
    private sealed class BackendFixture
    {
        private const string CodePulContent = "kamek code";

        public MockFileSystem FileSystem { get; } = new();
        public string Root { get; }
        public string StateFile { get; }
        public string BaseFolder { get; }
        public string RetroFolder { get; }
        public string RetroRewindSource { get; }

        private readonly List<string> _productRecords = [];

        public BackendFixture()
        {
            Root = FileSystem.Path.Combine(FileSystem.Directory.GetCurrentDirectory(), "WiiCompiled");
            StateFile = FileSystem.Path.Combine(Root, "install-state.json");
            BaseFolder = FileSystem.Path.Combine(Root, "Install", "Base");
            RetroFolder = FileSystem.Path.Combine(Root, "Install", "RetroRewind");
            RetroRewindSource = FileSystem.Path.Combine(FileSystem.Directory.GetCurrentDirectory(), "RetroRewind6");
            FileSystem.Directory.CreateDirectory(Root);
            FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(RetroRewindSource, "Binaries"));
            FileSystem.File.WriteAllText(FileSystem.Path.Combine(RetroRewindSource, "Binaries", "Code.pul"), CodePulContent);
        }

        public BackendFixture WithBase()
        {
            FileSystem.Directory.CreateDirectory(BaseFolder);
            FileSystem.File.WriteAllText(FileSystem.Path.Combine(BaseFolder, "WiiCompiled"), "elf");
            _productRecords.Add(Record("base", BaseFolder, "WiiCompiled"));
            WriteState();
            return this;
        }

        public BackendFixture WithRetroRewind(bool codePulMatches, bool writeBuildRecord = true)
        {
            FileSystem.Directory.CreateDirectory(RetroFolder);
            FileSystem.File.WriteAllText(FileSystem.Path.Combine(RetroFolder, "RetroRewind"), "elf");
            if (writeBuildRecord)
            {
                var sha = codePulMatches ? Sha256(CodePulContent) : Sha256("an older Code.pul");
                FileSystem.File.WriteAllText(
                    FileSystem.Path.Combine(RetroFolder, "local-build.json"),
                    $$$"""{"SchemaVersion":1,"Profile":"retro-rewind","CodePulSha256":"{{{sha}}}"}"""
                );
            }
            _productRecords.Add(Record("retro-rewind", RetroFolder, "RetroRewind"));
            WriteState();
            return this;
        }

        public RecompProductsEvent Inspect(string? retroRewindFolder = "")
        {
            var inspector = new RecompLinuxProductInspector(FileSystem, NullLogger<RecompLinuxProductInspector>.Instance);
            return inspector.Inspect(StateFile, "0.2.27", Root, retroRewindFolder == "" ? RetroRewindSource : retroRewindFolder);
        }

        private void WriteState()
        {
            FileSystem.Directory.CreateDirectory(Root);
            FileSystem.File.WriteAllText(
                StateFile,
                $$$"""{"SchemaVersion":1,"Workspace":"w","Products":[{{{string.Join(',', _productRecords)}}}]}"""
            );
        }

        private static string Record(string profile, string folder, string executable) =>
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    Profile = profile,
                    InstallDirectory = folder,
                    ExecutableName = executable,
                }
            );

        private static string Sha256(string content) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }
}
