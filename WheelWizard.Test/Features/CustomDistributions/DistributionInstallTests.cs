using System.IO.Compression;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.CustomDistributions.Domain;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Services;

namespace WheelWizard.Test.Features.CustomDistributions;

public class DistributionInstallTests
{
    [Fact]
    public async Task StableInstall_ExtractsThroughInjectedFilesystem_AndReportsProgressWithoutUi()
    {
        var fixture = new Fixture();
        fixture.Archive = Zip(
            ("RetroRewind6/version.txt", "6.0.0"),
            ("RetroRewind6/data.bin", "payload"),
            ("riivolution/RetroRewind6.xml", "xml")
        );
        var progress = new Collector();

        var result = await fixture.Stable.InstallAsync(new(progress));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("payload", fixture.Fs.File.ReadAllText(fixture.Fs.Path.Combine(fixture.Paths.RetroRewindFolderPath, "data.bin")));
        Assert.Equal("6.0.0", fixture.Stable.GetCurrentVersion()!.ToString());
        Assert.False(fixture.Fs.Directory.Exists(fixture.Paths.DownloadFolderPath));
        Assert.Contains(progress.Values, value => value.Percent == 100);
    }

    [Fact]
    public async Task BetaInstall_PreservesStableXml_AndWritesItsOwnManifest()
    {
        var fixture = new Fixture();
        fixture.Fs.Directory.CreateDirectory(fixture.Fs.Path.GetDirectoryName(fixture.Paths.XmlFilePath)!);
        fixture.Fs.File.WriteAllText(fixture.Paths.XmlFilePath, "stable xml");
        fixture.Archive = Zip(
            ("RRBeta/data.bin", "beta"),
            ("riivolution/RRBeta.xml", "beta xml"),
            ("riivolution/RetroRewind6.xml", "unwanted")
        );

        var result = await fixture.Beta.InstallAsync(new());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("stable xml", fixture.Fs.File.ReadAllText(fixture.Paths.XmlFilePath));
        Assert.Equal("beta xml", fixture.Fs.File.ReadAllText(fixture.Paths.BetaXmlFilePath));
        Assert.Contains("data.bin", fixture.Fs.File.ReadAllText(fixture.Paths.BetaManifestFilePath));
        Assert.DoesNotContain("RetroRewind6.xml", fixture.Fs.File.ReadAllText(fixture.Paths.BetaManifestFilePath));
        Assert.False(fixture.Fs.Directory.Exists(fixture.Paths.BetaDownloadFolderPath));
    }

    [Fact]
    public async Task StableArchiveTraversal_IsRejectedBeforePublishingInstallation()
    {
        var fixture = new Fixture();
        fixture.Archive = Zip(("../escaped.txt", "outside"));

        Assert.True((await fixture.Stable.InstallAsync(new())).IsFailure);

        Assert.False(
            fixture.Fs.File.Exists(
                fixture.Fs.Path.Combine(fixture.Fs.Path.GetDirectoryName(fixture.Paths.DownloadFolderPath)!, "escaped.txt")
            )
        );
        Assert.False(fixture.Fs.Directory.Exists(fixture.Paths.RetroRewindFolderPath));
    }

    [Fact]
    public async Task BetaPasswordDismissal_ReturnsFailureAndCleansTemporaryFiles()
    {
        var fixture = new Fixture();
        fixture.Archive = Zip(("RRBeta/data.bin", "beta"));
        fixture.Prompts.RequestBetaPasswordAsync().Returns((string?)null);

        var result = await fixture.Beta.InstallAsync(new());

        Assert.True(result.IsFailure);
        Assert.Contains("Password", result.Error.Message);
        Assert.False(fixture.Fs.Directory.Exists(fixture.Paths.BetaDownloadFolderPath));
        Assert.False(fixture.Fs.File.Exists(fixture.Paths.BetaManifestFilePath));
    }

    [Fact]
    public async Task DownloadCancellation_PropagatesTokenAndEndsCancellablePhase()
    {
        var downloads = Substitute.For<IDownloadService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        downloads
            .DownloadAsync(
                "https://example.com/archive.zip",
                "archive.zip",
                false,
                Arg.Any<IProgress<DownloadProgress>>(),
                cancellation.Token
            )
            .Returns(Task.FromCanceled<OperationResult<string>>(cancellation.Token));
        var progress = new Collector();

        var result = await downloads.DownloadDistributionAsync(
            "https://example.com/archive.zip",
            "archive.zip",
            new(progress, cancellation.Token)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(new bool?[] { true, false }, progress.Values.Select(value => value.CanCancel));
    }

    private static byte[] Zip(params (string Name, string Contents)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open());
                writer.Write(entry.Contents);
            }
        return stream.ToArray();
    }

    private sealed class Collector : IProgress<DistributionProgress>
    {
        public List<DistributionProgress> Values { get; } = [];

        public void Report(DistributionProgress value)
        {
            lock (Values)
                Values.Add(value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledInstallation_IsNotReportedAsCompleted(bool beta)
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture
            .Downloads.DownloadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<DownloadProgress>>(),
                cancellation.Token
            )
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<OperationResult<string>>(cancellation.Token);
            });
        IDistribution distribution = beta ? fixture.Beta : fixture.Stable;

        var result = await distribution.InstallAsync(new(CancellationToken: cancellation.Token));

        Assert.True(result.IsFailure);
        Assert.False(fixture.Fs.Directory.Exists(beta ? fixture.Paths.BetaFolderPath : fixture.Paths.RetroRewindFolderPath));
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new();
        public CustomDistributionPaths Paths { get; }
        public IDistributionPrompts Prompts { get; } = Substitute.For<IDistributionPrompts>();
        public IDownloadService Downloads { get; } = Substitute.For<IDownloadService>();
        public byte[] Archive { get; set; } = [];
        public RetroRewind Stable { get; }
        public RetroRewindBeta Beta { get; }

        public Fixture()
        {
            var root = Path.Combine(Path.GetTempPath(), "distribution-fixture");
            Fs.Directory.CreateDirectory(root);
            var location = Substitute.For<IApplicationDataLocation>();
            location.DirectoryPath.Returns(Fs.Path.Combine(root, "app"));
            var dolphin = Substitute.For<IDolphinPaths>();
            dolphin.UserFolderPath.Returns(Fs.Path.Combine(root, "dolphin"));
            dolphin.WiiFolderPath.Returns(Fs.Path.Combine(root, "nand"));
            dolphin.LoadFolderPath.Returns(Fs.Path.Combine(root, "load"));
            Paths = new(location, dolphin, Fs);
            var downloads = Downloads;
            downloads
                .DownloadAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<IProgress<DownloadProgress>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    var path = call.ArgAt<string>(1);
                    Fs.Directory.CreateDirectory(Fs.Path.GetDirectoryName(path)!);
                    Fs.File.WriteAllBytes(path, Archive);
                    return Ok(path);
                });
            var api = Substitute.For<IRetroRewindApi>();
            api.Ping().Returns("online");
            api.GetInstallUrl().Returns("https://example.com/archive.zip");
            api.GetVersionFile().Returns("6.0.0");
            var caller = Substitute.For<IApiCaller<IRetroRewindApi>>();
            caller
                .CallApiAsync(Arg.Any<Expression<Func<IRetroRewindApi, Task<string>>>>())
                .Returns(async call => Ok(await call.Arg<Expression<Func<IRetroRewindApi, Task<string>>>>().Compile()(api)));
            Prompts.RequestBetaPasswordAsync().Returns("password");
            var settings = Substitute.For<ISettingsManager>();
            Stable = new(Fs, caller, NullLogger<IDistribution>.Instance, settings, downloads, Paths, dolphin, Prompts);
            Beta = new(Fs, NullLogger<IDistribution>.Instance, settings, downloads, Paths, Prompts);
        }
    }
}
