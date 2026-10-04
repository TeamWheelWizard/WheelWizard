using Microsoft.Extensions.Logging;

namespace WheelWizard.GameBanana;

public interface IGameBananaMediaService
{
    Task<OperationResult<byte[]>> GetImageAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class GameBananaMediaService(IHttpClientFactory clients, ILogger<GameBananaMediaService> logger) : IGameBananaMediaService
{
    public const string ClientName = "GameBananaMedia";

    public async Task<OperationResult<byte[]>> GetImageAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to load GameBanana preview image");
            return new OperationError { Message = "Unable to load preview image.", Exception = exception };
        }
    }
}
