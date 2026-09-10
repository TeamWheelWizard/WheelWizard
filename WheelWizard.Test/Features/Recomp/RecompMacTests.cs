using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.GitHub.Domain;
using WheelWizard.Recomp;
using Xunit;

namespace WheelWizard.Test.Features.Recomp;

public class RecompMacTests
{
    [Fact]
    public void MacReleaseSelectionDoesNotOfferWindowsInstaller()
    {
        var windows = new GithubRelease
        {
            TagName = "v9.0.0",
            Assets = [new GithubAsset { Name = "WiiCompiled-Setup.exe", BrowserDownloadUrl = "https://example.com/windows" }],
        };
        var mac = new GithubRelease
        {
            TagName = "v1.0.0",
            Assets = [new GithubAsset { Name = "WiiCompiled-Setup-macos-arm64.run", BrowserDownloadUrl = "https://example.com/mac" }],
        };
        Assert.Equal("v1.0.0", RecompReleaseResolver.FindLatest([windows, mac], "WiiCompiled-Setup-macos-arm64.run")?.TagName);
        Assert.Null(RecompReleaseResolver.FindLatest([windows], "WiiCompiled-Setup-macos-arm64.run"));
    }

    [Fact]
    public async Task MacRunnerPassesLiteralPathsToSetupWithoutShellExpansion()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard argv " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "a \"quoted\" setup.run");
        try
        {
            await File.WriteAllTextAsync(script, "#!/bin/bash\nprintf '%s\\n' \"$@\"\n");
            var game = "/Users/Zoë/a \"quoted\" \\ game $(touch NEVER).rvz";
            var install = "/Users/Zoë/Application Support/Install";
            var arguments = RecompSetupCommandBuilder.BuildSilentInstallArguments(
                new()
                {
                    GameFilePath = game,
                    InstallFolderPath = install,
                    Portable = true,
                }
            );
            var output = new List<string>();
            var runner = new RecompProcessRunner(NullLogger<RecompProcessRunner>.Instance);
            var result = await runner.RunAsync(script, arguments, null, output.Add);
            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.Value);
            Assert.Equal(game, output[2]);
            Assert.Equal(install, output[4]);
            Assert.Contains("--portable", output);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MacOffersMetal()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        Assert.Equal(new[] { "metal" }, RecompVideoConfig.OfferedGraphicsApis);
        Assert.Equal("Metal", RecompVideoConfig.DescribeGraphicsApi("metal"));
    }

    [Fact]
    public async Task MacCancellationAllowsTheHelperToCleanUp()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-cancel-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "setup.run");
        string? signal = null;
        var output = new List<string>();
        using var cancellation = new CancellationTokenSource();
        try
        {
            await File.WriteAllTextAsync(
                script,
                "#!/bin/bash\nprintf '%s\\n' \"$MKWCOMPILED_CANCEL_FILE\"\nwhile [[ ! -f \"$MKWCOMPILED_CANCEL_FILE\" ]]; do sleep 0.05; done\necho cleaned-up\nexit 1\n"
            );
            var runner = new RecompProcessRunner(NullLogger<RecompProcessRunner>.Instance);
            var result = await runner.RunAsync(
                script,
                "",
                null,
                line =>
                {
                    output.Add(line);
                    if (signal is null)
                    {
                        signal = line;
                        cancellation.Cancel();
                    }
                },
                cancellation.Token
            );
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value);
            Assert.Contains("cleaned-up", output);
            Assert.False(File.Exists(signal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
