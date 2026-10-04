using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.GameBanana;

namespace WheelWizard.Test.Features;

public sealed class GameBananaMediaTests
{
    [Fact]
    public async Task SuccessfulImage_ReturnsBytesAndDisposesResponseContent()
    {
        var content = new ByteArrayContent([1, 2, 3]);
        var service = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var result = await service.GetImageAsync("https://example.test/preview.png");

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task MissingImage_ReturnsFailure()
    {
        var service = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await service.GetImageAsync("https://example.test/missing.png");

        Assert.True(result.IsFailure);
        Assert.IsType<HttpRequestException>(result.Error.Exception);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        var service = Create(token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetImageAsync("https://example.test/image.png", cancellation.Token)
        );
    }

    private static GameBananaMediaService Create(Func<CancellationToken, Task<HttpResponseMessage>> send)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(GameBananaMediaService.ClientName).Returns(_ => new HttpClient(new Handler(send)));
        return new(clients, NullLogger<GameBananaMediaService>.Instance);
    }

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(cancellationToken);
    }
}
