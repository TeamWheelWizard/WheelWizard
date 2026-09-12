using Microsoft.Extensions.Logging;

namespace WheelWizard.Recomp;

/// <summary>
/// Answers whether the shared Retro-WFC payload can currently be downloaded. The setup host is the only
/// thing that ever downloads and verifies the payload; this probe exists so WheelWizard can decide, before
/// starting a long install, whether to ask the user for an offline-only build instead of failing later.
/// </summary>
public interface IRecompRetroWfcPayloadProbe
{
    /// <summary>
    /// True when the payload endpoint answered with a success status. Never throws: any failure, including
    /// a timeout, simply reads as unreachable.
    /// </summary>
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RecompRetroWfcPayloadProbe(IHttpClientFactory httpClientFactory, ILogger<RecompRetroWfcPayloadProbe> logger)
    : IRecompRetroWfcPayloadProbe
{
    /// <summary>
    /// The fixed payload endpoint the setup host downloads from. It must stay identical to the value pinned
    /// in the recomp's <c>RetroWfcPayload.CurrentRetroWfcPayloadUri</c>, since probing anything else says
    /// nothing about whether the host's own download will succeed. Plain HTTP is what the service offers
    /// and what the host pins; that is safe here because this probe decides nothing about trust. The host
    /// verifies the payload's RSA signature itself, so a forged answer can at worst make the host attempt
    /// a download that its own verification then rejects.
    /// </summary>
    public const string PayloadUri = "http://nas.play.rwfc.net/payload?g=RMCPD00";

    // A dead endpoint times out rather than refusing, so a short cap is what keeps the home page and the
    // install flow from stalling on every status refresh while the service is down.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);

    // A status refresh and the install that follows it should not each pay for their own probe.
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromSeconds(60);

    private readonly object _cacheLock = new();
    private DateTimeOffset _cachedAtUtc = DateTimeOffset.MinValue;
    private bool _cachedResult;

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (DateTimeOffset.UtcNow - _cachedAtUtc < ResultLifetime)
                return _cachedResult;
        }

        var reachable = await ProbeAsync(cancellationToken);

        lock (_cacheLock)
        {
            _cachedResult = reachable;
            _cachedAtUtc = DateTimeOffset.UtcNow;
        }

        return reachable;
    }

    private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(ProbeTimeout);

            // Headers only: the payload is a multi-megabyte binary and its bytes are the host's business.
            var client = httpClientFactory.CreateClient(RecompSetupDownloader.HttpClientName);
            using var response = await client.GetAsync(PayloadUri, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("The Retro-WFC payload endpoint answered {StatusCode}", (int)response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("The Retro-WFC payload endpoint is unreachable: {Message}", exception.Message);
            return false;
        }
    }
}
