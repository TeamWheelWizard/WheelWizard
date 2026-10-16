using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using WheelWizard.DolphinInstaller;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features;

public class DolphinReleaseClientTests
{
    private const string Response = """
        {"shortrev": "2609a", "hash": "409881c3", "artifacts": [
          {"system": "Linux x86_64 (Flatpak)", "url": "https://dl.dolphin-emu.org/releases/2609a/dolphin-2609a-x86_64.flatpak"},
          {"system": "macOS (ARM/Intel Universal)", "url": "https://dl.dolphin-emu.org/releases/2609a/dolphin-2609a-universal.dmg"},
          {"system": "Windows arm64", "url": "https://dl.dolphin-emu.org/releases/2609a/dolphin-2609a-arm64.7z"},
          {"system": "Windows x64", "url": "https://dl.dolphin-emu.org/releases/2609a/dolphin-2609a-x64.7z"},
          {"system": "Windows x64", "url": "http://evil.example/dolphin.7z"}
        ]}
        """;

    [Fact]
    public async Task LatestRelease_KeepsOnlyOfficialHttpsDownloads_AndPicksThisMachinesBuild()
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(true);
        environment.OSArchitecture.Returns(Architecture.X64);
        var client = new DolphinReleaseClient(Clients(Response), environment);

        var release = await client.GetLatestReleaseAsync();

        Assert.True(release.IsSuccess);
        Assert.Equal("2609a", release.Value.ShortRevision);
        Assert.Equal(4, release.Value.Artifacts.Count);
        Assert.Equal("https://dl.dolphin-emu.org/releases/2609a/dolphin-2609a-x64.7z", client.SelectArtifact(release.Value)!.Url);

        environment.OSArchitecture.Returns(Architecture.Arm64);
        Assert.EndsWith("arm64.7z", client.SelectArtifact(release.Value)!.Url);

        environment.IsWindows.Returns(false);
        environment.IsMacOS.Returns(true);
        Assert.EndsWith("universal.dmg", client.SelectArtifact(release.Value)!.Url);

        environment.IsMacOS.Returns(false);
        environment.IsLinux.Returns(true);
        Assert.Null(client.SelectArtifact(release.Value));
    }

    [Theory]
    [InlineData("https://dl.dolphin-emu.org/releases/x.7z", true)]
    [InlineData("http://dl.dolphin-emu.org/releases/x.7z", false)]
    [InlineData("https://dl.dolphin-emu.org.evil.example/x.7z", false)]
    [InlineData("not a url", false)]
    public void OnlyOfficialHttpsLinks_AreFollowed(string url, bool expected) =>
        Assert.Equal(expected, DolphinReleaseClient.IsOfficialDownload(url));

    private static IHttpClientFactory Clients(string body)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new StaticHandler(body)));
        return factory;
    }

    private sealed class StaticHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
