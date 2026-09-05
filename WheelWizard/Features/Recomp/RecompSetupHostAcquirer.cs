using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.GitHub;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// Gets a setup host onto disk: finds the newest release for this platform, downloads its setup into the
/// cache, and proves the file is that release by asking it for its version. This is the part of the
/// install flow that is the same on every platform; what the setup is then told to do is not, and lives
/// in the per-platform install services.
/// </summary>
public sealed class RecompSetupHostAcquirer(
    IRecompProcessRunner processRunner,
    IRecompSetupDownloader downloader,
    IGitHubSingletonService gitHubService,
    IFileSystem fileSystem,
    ILogger<RecompSetupHostAcquirer> logger
)
{
    // How the phases of an install divide up the 0-100 progress bar.
    public const int DownloadPercentFloor = 5;
    public const int SetupPercentFloor = 35;

    public async Task<RecompRelease?> TryGetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var releasesResult = await gitHubService.GetReleasesAsync(
            RecompReleaseResolver.RepositoryOwner,
            RecompReleaseResolver.RepositoryName,
            count: 100
        );
        if (releasesResult.IsFailure)
        {
            logger.LogWarning("Could not retrieve the recomp releases: {Message}", releasesResult.Error.Message);
            return null;
        }

        return RecompReleaseResolver.FindLatest(releasesResult.Value);
    }

    /// <summary>
    /// Returns the path of a cached setup that reports <paramref name="release"/>'s version, downloading it
    /// first when the cache has none. Setups of other releases are pruned once this one is known good.
    /// </summary>
    public async Task<OperationResult<string>> EnsureSetupDownloadedAsync(
        RecompRelease release,
        string cacheFolderPath,
        IProgress<RecompInstallProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var cachedSetupPath = fileSystem.Path.Combine(cacheFolderPath, RecompPlatform.CachedSetupFileName(release.TagName));
        if (IsUsableFile(cachedSetupPath) && await SetupMatchesVersionAsync(cachedSetupPath, release.TagName, cancellationToken))
        {
            PruneCachedSetupsExcept(cacheFolderPath, cachedSetupPath);
            return Ok(cachedSetupPath);
        }

        var downloadMessage = t("progress.recomp_downloading_setup");
        Report(progress, downloadMessage, DownloadPercentFloor);

        var downloadProgress = new DelegateProgress<int>(percent =>
            Report(progress, downloadMessage, DownloadPercentFloor + (percent * (SetupPercentFloor - DownloadPercentFloor) / 100))
        );

        var downloadResult = await downloader.DownloadAsync(release.SetupDownloadUrl, cachedSetupPath, downloadProgress, cancellationToken);
        if (downloadResult.IsFailure)
            return downloadResult.Error;

        if (!IsUsableFile(cachedSetupPath))
            return Fail("The downloaded WiiCompiled setup is missing or empty.");

        MakeExecutable(cachedSetupPath);
        if (!await SetupMatchesVersionAsync(cachedSetupPath, release.TagName, cancellationToken))
        {
            var removed = DeleteInvalidSetup(cachedSetupPath);
            return removed
                ? Fail($"The downloaded WiiCompiled setup did not report release {release.TagName}.")
                : Fail($"The downloaded WiiCompiled setup did not report release {release.TagName} and could not be removed.");
        }

        PruneCachedSetupsExcept(cacheFolderPath, cachedSetupPath);
        return Ok(cachedSetupPath);
    }

    /// <summary>
    /// Whether the setup at <paramref name="setupFilePath"/> reports exactly <paramref name="expectedVersion"/>.
    /// Both setups answer <c>--version</c> with a bare semantic version on stdout.
    /// </summary>
    public async Task<bool> SetupMatchesVersionAsync(string setupFilePath, string? expectedVersion, CancellationToken cancellationToken)
    {
        if (!RecompVersion.TryParse(expectedVersion, out var expected))
            return false;

        string? versionText = null;
        var runResult = await processRunner.RunAsync(
            setupFilePath,
            RecompSetupCommandBuilder.BuildVersionArguments(),
            workingDirectory: null,
            line =>
            {
                if (RecompVersion.TryParse(line, out var version))
                    versionText = version.ToString();
            },
            cancellationToken
        );

        return runResult.IsSuccess
            && runResult.Value == 0
            && RecompVersion.TryParse(versionText, out var cachedVersion)
            && cachedVersion.ComparePrecedenceTo(expected) == 0;
    }

    public static bool VersionsMatch(string? first, string? second) =>
        RecompVersion.TryParse(first, out var parsedFirst)
        && RecompVersion.TryParse(second, out var parsedSecond)
        && parsedFirst.ComparePrecedenceTo(parsedSecond) == 0;

    public bool IsUsableFile(string filePath)
    {
        try
        {
            return fileSystem.File.Exists(filePath) && fileSystem.FileInfo.New(filePath).Length > 0;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to inspect the cached recomp setup at {Path}", filePath);
            return false;
        }
    }

    /// <summary>
    /// A downloaded AppImage arrives without its execute bit. Windows has no such bit, and a failure here
    /// is left for the version check to surface as "the setup did not report its version".
    /// </summary>
    public void MakeExecutable(string filePath)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = fileSystem.File.GetUnixFileMode(filePath);
            fileSystem.File.SetUnixFileMode(filePath, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not mark the recomp setup at {Path} as executable", filePath);
        }
    }

    public static void Report(IProgress<RecompInstallProgress>? progress, string message, int percent) =>
        progress?.Report(new(message, Math.Clamp(percent, 0, 100)));

    /// <summary>
    /// Drops setups cached for other releases. Each one is hundreds of megabytes and the cache is only
    /// ever read for the release currently being installed, so keeping them meant every update
    /// permanently cost the user another installer's worth of disk. Failure here is deliberately
    /// silent: it is disk hygiene, never a reason to fail an install that has already succeeded.
    /// </summary>
    private void PruneCachedSetupsExcept(string cacheFolderPath, string keepFilePath)
    {
        try
        {
            if (!fileSystem.Directory.Exists(cacheFolderPath))
                return;
            foreach (var candidate in fileSystem.Directory.EnumerateFiles(cacheFolderPath, RecompPlatform.CachedSetupSearchPattern))
            {
                if (string.Equals(candidate, keepFilePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    fileSystem.File.Delete(candidate);
                    logger.LogInformation("Removed the superseded cached WiiCompiled setup {Path}", candidate);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(exception, "Could not remove the cached WiiCompiled setup {Path}", candidate);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Could not enumerate the WiiCompiled setup cache for pruning.");
        }
    }

    private bool DeleteInvalidSetup(string setupFilePath)
    {
        try
        {
            if (fileSystem.File.Exists(setupFilePath))
                fileSystem.File.Delete(setupFilePath);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not remove invalid recomp setup at {Path}", setupFilePath);
            return false;
        }
    }

    /// <summary>
    /// Forwards progress synchronously, so the single <see cref="Progress{T}"/> the launcher owns stays the
    /// only place where marshalling to the UI thread happens.
    /// </summary>
    private sealed class DelegateProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
