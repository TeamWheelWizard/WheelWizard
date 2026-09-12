using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.Shared.Downloads;

namespace WheelWizard.Test.Shared.Downloads;

public sealed class DownloadServiceTests
{
    private readonly MockFileSystem _files = new();
    private readonly string _destination = Path.GetFullPath("downloads/archive.zip");

    [Fact]
    public async Task Download_UsesExactPathAndReportsBytes()
    {
        var progress = new List<DownloadProgress>();
        var service = Create((_, _) => Task.FromResult(Response([1, 2, 3])));
        var result = await service.DownloadAsync("https://example.test/file", _destination, true, new Reporter(progress.Add));

        Assert.True(result.IsSuccess);
        Assert.Equal(_destination, result.Value);
        Assert.Equal(new byte[] { 1, 2, 3 }, _files.File.ReadAllBytes(result.Value));
        Assert.Equal(3, progress[^1].BytesReceived);
        Assert.Equal(100, progress[^1].Percentage);
        Assert.Single(_files.Directory.GetFiles(Path.GetDirectoryName(_destination)!));
    }

    [Fact]
    public async Task Download_RetriesHttpFailure_ThenSucceeds()
    {
        var attempts = 0;
        var service = Create(
            (_, _) => Task.FromResult(++attempts == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Response([4]))
        );

        var result = await service.DownloadAsync("https://example.test/file", _destination, true);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempts);
        Assert.Equal(new byte[] { 4 }, _files.File.ReadAllBytes(_destination));
    }

    [Fact]
    public async Task Download_FailurePreservesExistingDestination()
    {
        _files.Directory.CreateDirectory(Path.GetDirectoryName(_destination)!);
        _files.File.WriteAllBytes(_destination, [9]);
        var attempts = 0;
        var service = Create(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
        );

        var result = await service.DownloadAsync("https://example.test/file", _destination, true);

        Assert.True(result.IsFailure);
        Assert.Equal(2, attempts);
        Assert.Equal(new byte[] { 9 }, _files.File.ReadAllBytes(_destination));
        Assert.Single(_files.Directory.GetFiles(Path.GetDirectoryName(_destination)!));
    }

    [Fact]
    public async Task Download_CancellationPreservesExistingDestinationAndDoesNotRetry()
    {
        _files.Directory.CreateDirectory(Path.GetDirectoryName(_destination)!);
        _files.File.WriteAllBytes(_destination, [9]);
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var service = Create(
            (_, token) =>
            {
                attempts++;
                cancellation.Cancel();
                return Task.FromCanceled<HttpResponseMessage>(token);
            }
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync("https://example.test/file", _destination, true, cancellationToken: cancellation.Token)
        );

        Assert.Equal(1, attempts);
        Assert.Equal(new byte[] { 9 }, _files.File.ReadAllBytes(_destination));
        Assert.Single(_files.Directory.GetFiles(Path.GetDirectoryName(_destination)!));
    }

    [Fact]
    public async Task Download_ResolvesServerFilenameAndRedirectExtension()
    {
        var service = Create(
            (_, _) =>
            {
                var response = Response([1]);
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "release" };
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.test/release.7z");
                return Task.FromResult(response);
            }
        );

        var result = await service.DownloadAsync("https://example.test/file", _destination);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(_destination)!, "release.7z"), result.Value);
    }

    [Fact]
    public async Task Download_DiscardsServerDirectoriesAndStaysInsideDestinationFolder()
    {
        var service = Create(
            (_, _) =>
            {
                var response = Response([1]);
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = "../outside.zip",
                };
                return Task.FromResult(response);
            }
        );

        var result = await service.DownloadAsync("https://example.test/file", _destination);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(_destination)!, "outside.zip"), result.Value);
        Assert.False(_files.File.Exists(Path.GetFullPath("outside.zip")));
    }

    [Fact]
    public async Task Download_CancellationDuringWriteRemovesPartialFile()
    {
        _files.Directory.CreateDirectory(Path.GetDirectoryName(_destination)!);
        _files.File.WriteAllBytes(_destination, [9]);
        using var cancellation = new CancellationTokenSource();
        var service = Create((_, _) => Task.FromResult(Response(new byte[20000])));
        var progress = new Reporter(value =>
        {
            if (value.BytesReceived > 0)
                cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync("https://example.test/file", _destination, true, progress, cancellation.Token)
        );

        Assert.Equal(new byte[] { 9 }, _files.File.ReadAllBytes(_destination));
        Assert.Single(_files.Directory.GetFiles(Path.GetDirectoryName(_destination)!));
    }

    private DownloadService Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(DownloadService.ClientName).Returns(_ => new HttpClient(new Handler(send)));
        return new(
            factory,
            _files,
            System.TimeProvider.System,
            new DownloadOptions(2, TimeSpan.Zero),
            NullLogger<DownloadService>.Instance
        );
    }

    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class Reporter(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
