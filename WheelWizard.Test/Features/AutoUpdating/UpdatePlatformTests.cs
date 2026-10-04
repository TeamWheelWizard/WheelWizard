using System.Diagnostics;
using System.Runtime.InteropServices;
using Testably.Abstractions.Testing;
using WheelWizard.AutoUpdating;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Test.Features.AutoUpdating;

public class UpdatePlatformTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedDownload_StartsScriptBeforeExiting(bool windows)
    {
        var fixture = new Fixture(windows);
        fixture
            .Downloads.DownloadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                true,
                Arg.Any<IProgress<DownloadProgress>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                fixture.Fs.File.WriteAllText(call.ArgAt<string>(1), "new");
                return Ok(call.ArgAt<string>(1));
            });

        Assert.True((await fixture.Platform.ExecuteUpdateAsync("https://example.test/update")).IsSuccess);

        Received.InOrder(() =>
        {
            fixture.Processes.Start(Arg.Is<ProcessStartInfo>(info => info.FileName == (windows ? "powershell.exe" : "/usr/bin/env")));
            fixture.Application.Exit(0);
        });
        var scriptPath = fixture.Fs.Path.Combine(fixture.Directory, windows ? "update.ps1" : "update.sh");
        var script = fixture.Fs.File.ReadAllText(scriptPath);
        Assert.Contains(windows ? "Rename-Item" : "mv ", script);
        Assert.Contains(windows ? "WheelWizard_new.exe" : "WheelWizard_new", script);
        Assert.Equal("old", fixture.Fs.File.ReadAllText(fixture.Application.ExecutablePath!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDownload_DoesNotStartScriptOrExit(bool windows)
    {
        var fixture = new Fixture(windows);
        fixture
            .Downloads.DownloadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                true,
                Arg.Any<IProgress<DownloadProgress>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Fail("offline"));

        var result = await fixture.Platform.ExecuteUpdateAsync("https://example.test/update");

        Assert.True(result.IsFailure);
        Assert.Equal("offline", result.Error.Message);
        Assert.Empty(fixture.Processes.ReceivedCalls());
        fixture.Application.DidNotReceive().Exit(Arg.Any<int>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledDownload_DoesNotStartScriptOrExit(bool windows)
    {
        var fixture = new Fixture(windows);
        using var cancellation = new CancellationTokenSource();
        fixture
            .Downloads.DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<IProgress<DownloadProgress>>(), cancellation.Token)
            .Returns(call =>
            {
                cancellation.Cancel();
                fixture.Fs.File.WriteAllText(call.ArgAt<string>(1), "new");
                return Ok(call.ArgAt<string>(1));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Platform.ExecuteUpdateAsync("https://example.test/update", cancellationToken: cancellation.Token)
        );

        Assert.Empty(fixture.Processes.ReceivedCalls());
        fixture.Application.DidNotReceive().Exit(Arg.Any<int>());
    }

    [Fact]
    public async Task ElevationChoice_RestartsInsteadOfDownloading()
    {
        var fixture = new Fixture(true);
        fixture.Application.IsAdministrator.Returns(false);
        fixture.Presentation.ConfirmElevationAsync().Returns(true);

        Assert.True((await fixture.Platform.ExecuteUpdateAsync("https://example.test/update")).IsSuccess);

        fixture
            .Processes.Received(1)
            .Start(Arg.Is<ProcessStartInfo>(info => info.Verb == "runas" && info.FileName == fixture.Application.ExecutablePath));
        fixture.Application.Received(1).Exit(0);
        Assert.Empty(fixture.Downloads.ReceivedCalls());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedScriptStart_DoesNotExit(bool windows)
    {
        var fixture = new Fixture(windows);
        fixture
            .Downloads.DownloadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                true,
                Arg.Any<IProgress<DownloadProgress>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                fixture.Fs.File.WriteAllText(call.ArgAt<string>(1), "new");
                return Ok(call.ArgAt<string>(1));
            });
        fixture.Processes.When(value => value.Start(Arg.Any<ProcessStartInfo>())).Do(_ => throw new IOException("blocked"));

        Assert.True((await fixture.Platform.ExecuteUpdateAsync("https://example.test/update")).IsFailure);

        fixture.Application.DidNotReceive().Exit(Arg.Any<int>());
    }

    [Theory]
    [InlineData(Architecture.X64, "WheelWizard_Linux")]
    [InlineData(Architecture.Arm64, "WheelWizard_arm64_Linux")]
    public void LinuxAssetSelection_UsesInjectedProcessArchitecture(Architecture architecture, string name)
    {
        var fixture = new Fixture(false);
        fixture.Application.Architecture.Returns(architecture);
        var release = new GithubRelease
        {
            TagName = "v1.0.0",
            Assets =
            [
                new() { Name = "WheelWizard_Linux", BrowserDownloadUrl = "https://example.test/WheelWizard_Linux" },
                new() { Name = "WheelWizard_arm64_Linux", BrowserDownloadUrl = "https://example.test/WheelWizard_arm64_Linux" },
            ],
        };
        Assert.Equal(name, fixture.Platform.GetAssetForCurrentPlatform(release)!.Name);
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; }
        public IDownloadService Downloads { get; } = Substitute.For<IDownloadService>();
        public IApplicationProcess Application { get; } = Substitute.For<IApplicationProcess>();
        public IProcessLauncher Processes { get; } = Substitute.For<IProcessLauncher>();
        public IUpdatePresentation Presentation { get; } = Substitute.For<IUpdatePresentation>();
        public IUpdatePlatform Platform { get; }
        public string Directory { get; }

        public Fixture(bool windows)
        {
            Fs = new MockFileSystem(options => options.SimulatingOperatingSystem(windows ? SimulationMode.Windows : SimulationMode.Linux));
            Directory = windows ? "C:\\Application Files" : "/application files";
            Fs.Directory.CreateDirectory(Directory);
            var executable = Fs.Path.Combine(Directory, windows ? "WheelWizard.exe" : "WheelWizard");
            Fs.File.WriteAllText(executable, "old");
            Application.ExecutablePath.Returns(executable);
            Application.WorkingDirectory.Returns(Directory);
            Application.IsAdministrator.Returns(true);
            Platform = windows
                ? new WindowsUpdatePlatform(Fs, Downloads, Application, Processes, Presentation)
                : new LinuxUpdatePlatform(Fs, Downloads, Application, Processes);
        }
    }
}
