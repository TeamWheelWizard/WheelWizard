using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Platform;

namespace WheelWizard.DolphinInstaller;

public sealed record DolphinReleaseArtifact(string System, string Url);

public sealed record DolphinRelease(string ShortRevision, string Hash, IReadOnlyList<DolphinReleaseArtifact> Artifacts);

public interface IDolphinReleaseClient
{
    /// <summary>The newest official Dolphin release, as published by dolphin-emu.org.</summary>
    Task<OperationResult<DolphinRelease>> GetLatestReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>The download for this machine, or <see langword="null"/> when Dolphin publishes none we can install.</summary>
    DolphinReleaseArtifact? SelectArtifact(DolphinRelease release);
}

public sealed class DolphinReleaseClient(IHttpClientFactory clients, IRuntimeEnvironment environment) : IDolphinReleaseClient
{
    // The "beta" track is the one Dolphin's own updater uses for releases; it is the only track that
    // answers and always points at the newest release (e.g. 2609a), never a dev build.
    public const string LatestReleaseUrl = "https://dolphin-emu.org/update/latest/beta/";
    public const string DownloadHost = "dl.dolphin-emu.org";

    public async Task<OperationResult<DolphinRelease>> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = clients.CreateClient(DownloadService.ClientName);
            await using var stream = await client.GetStreamAsync(LatestReleaseUrl, cancellationToken);
            var response = await JsonSerializer.DeserializeAsync<ReleaseResponse>(stream, cancellationToken: cancellationToken);
            if (response is null || string.IsNullOrWhiteSpace(response.ShortRevision) || response.Artifacts is null)
                return Fail("Dolphin's release information could not be read.");

            var artifacts = response
                .Artifacts.Where(artifact => !string.IsNullOrWhiteSpace(artifact.System) && IsOfficialDownload(artifact.Url))
                .Select(artifact => new DolphinReleaseArtifact(artifact.System!, artifact.Url!))
                .ToList();
            return new DolphinRelease(response.ShortRevision.Trim(), response.Hash ?? string.Empty, artifacts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new OperationError { Message = $"Could not reach dolphin-emu.org: {exception.Message}", Exception = exception };
        }
    }

    public DolphinReleaseArtifact? SelectArtifact(DolphinRelease release)
    {
        bool Matches(DolphinReleaseArtifact artifact, string system) =>
            artifact.System.StartsWith(system, StringComparison.OrdinalIgnoreCase);

        if (environment.IsWindows)
        {
            var system = environment.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "Windows arm64" : "Windows x64";
            return release.Artifacts.FirstOrDefault(artifact =>
                Matches(artifact, system) && artifact.Url.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
            );
        }

        if (environment.IsMacOS)
            return release.Artifacts.FirstOrDefault(artifact =>
                Matches(artifact, "macOS") && artifact.Url.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)
            );

        return null;
    }

    /// <summary>Only HTTPS links on Dolphin's own download host are ever followed.</summary>
    public static bool IsOfficialDownload(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, DownloadHost, StringComparison.OrdinalIgnoreCase);

    private sealed class ReleaseResponse
    {
        [JsonPropertyName("shortrev")]
        public string? ShortRevision { get; init; }

        [JsonPropertyName("hash")]
        public string? Hash { get; init; }

        [JsonPropertyName("artifacts")]
        public List<ReleaseArtifact>? Artifacts { get; init; }
    }

    private sealed class ReleaseArtifact
    {
        [JsonPropertyName("system")]
        public string? System { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }
}
