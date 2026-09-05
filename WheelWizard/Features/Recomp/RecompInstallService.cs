using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.GitHub;
using WheelWizard.Models.Enums;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

public interface IRecompInstallService : IDisposable
{
    /// <summary>
    /// True while this service owns an install, a pre-launch reconciliation or a running play session.
    /// A status read taken during one cannot see the truth: the operation holds this service's gate
    /// for its whole duration.
    /// </summary>
    bool OperationInFlight { get; }

    /// <summary>
    /// Whether an installed setup host is present on disk. A pure file probe: it consults neither
    /// the backend nor the network, so it is safe to read before showing UI.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Runs the installed <c>WiiCompiled-Setup.exe --version</c> host and returns its semantic version.
    /// WiiCompiled is Windows-only, so other platforms return <see langword="null"/>.
    /// </summary>
    Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <c>install-state.json</c>, compares it against the latest GitHub release, and asks
    /// <c>--check-products</c> whether the installed executables are still fresh.
    /// </summary>
    Task<WheelWizardStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>--check-products</c>, which reports per-product freshness without building anything.
    /// Every call asks the backend anew: the check hashes only compile inputs, so a fresh answer is
    /// cheap and also covers Retro Rewind changes made outside this service.
    /// </summary>
    Task<OperationResult<RecompProductsEvent>> CheckProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs a missing/newer setup release, or asks the installed setup host what needs doing and
    /// repairs only that. An asset-only Retro Rewind change reports <c>current</c> and does no work.
    /// </summary>
    /// <param name="confirmOfflineInstall">
    /// Consulted only when a fresh Retro Rewind build is needed and the Retro-WFC payload service is
    /// unreachable. Returning true builds without online play; false or <see langword="null"/> fails the
    /// install with an explanation instead. An installation that already embeds a payload never asks: the
    /// setup host falls back to its own verified copy.
    /// </param>
    Task<OperationResult> InstallAsync(
        IProgress<RecompInstallProgress>? progress = null,
        Func<Task<bool>>? confirmOfflineInstall = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Verifies product health immediately before launch and repairs only what the check demands.
    /// This never resolves or downloads a setup release.
    /// </summary>
    Task<OperationResult> ReconcileForLaunchAsync(
        IProgress<RecompInstallProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Starts the game through the launcher copy of the setup executable in the install directory,
    /// after a successful one-time <see cref="ReconcileForLaunchAsync"/> authorization.
    /// </summary>
    Task<OperationResult> LaunchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the WiiCompiled installation, its runtime user state and the setup cache. The recomp is
    /// a portable installation, which the contract says is uninstalled by deleting its directories:
    /// no registry entry exists and the Retro Rewind installation is deliberately left in place.
    /// </summary>
    Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RecompInstallService : IRecompInstallService
{
    // How the three phases of an install divide up the 0-100 progress bar.
    private const int DownloadPercentFloor = 5;
    private const int SetupPercentFloor = 35;

    private const int CurrentInstallStateSchemaVersion = 1;

    // A launch holds the gate for the whole play session, so "busy" almost always means "the game is running".
    private const string OperationAlreadyRunningMessage = "Another WiiCompiled operation is already running.";

    // The same situation seen from outside this process: the backend refused because its own
    // per-installation lock is held by a game, install or repair that WheelWizard does not own.
    private const string InstallationBusyMessage =
        "The WiiCompiled installation is busy. Close the running game, or wait for the current install or repair to finish, and try again.";

    // The backend serializes every operation on one installation with a lock file beside the
    // installation root. The name is part of the v1 contract; the digest inside it is not, so the
    // installation is probed by pattern rather than by recomputing the backend's own path.
    private const string OperationLockSearchPattern = ".mkwc-operation-*.lock";

    private const string RetroWfcUnavailableMessage =
        "The Retro WFC servers are not responding, so WiiCompiled cannot set up online play right now. Try again later, or install without online play.";

    private static readonly JsonSerializerOptions InstallStateJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IRecompEnvironment environment;
    private readonly IRecompProcessRunner processRunner;
    private readonly IRecompSetupDownloader downloader;
    private readonly IRecompRetroWfcPayloadProbe payloadProbe;
    private readonly IGitHubSingletonService gitHubService;
    private readonly IFileSystem fileSystem;
    private readonly ILogger<RecompInstallService> logger;

    // Set by a successful pre-launch reconciliation and consumed by the launch that follows it.
    // Both run under _operationGate, which is also what keeps the handoff a single-use token.
    private bool _launchReconciled;
    private int _disposed;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public RecompInstallService(
        IRecompEnvironment environment,
        IRecompProcessRunner processRunner,
        IRecompSetupDownloader downloader,
        IRecompRetroWfcPayloadProbe payloadProbe,
        IGitHubSingletonService gitHubService,
        IFileSystem fileSystem,
        ILogger<RecompInstallService> logger
    )
    {
        this.environment = environment;
        this.processRunner = processRunner;
        this.downloader = downloader;
        this.payloadProbe = payloadProbe;
        this.gitHubService = gitHubService;
        this.fileSystem = fileSystem;
        this.logger = logger;
    }

    // Probing the gate instead of taking it keeps a routine status refresh from being able to lose
    // the race against the user's own click on the button that refresh is about to draw.
    public bool OperationInFlight => _operationGate.CurrentCount == 0;

    public bool IsInstalled => fileSystem.File.Exists(environment.InstalledSetupFilePath);

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || !IsInstalled)
            return null;

        string? versionText = null;
        var runResult = await processRunner.RunAsync(
            environment.InstalledSetupFilePath,
            RecompSetupCommandBuilder.BuildVersionArguments(),
            workingDirectory: null,
            line =>
            {
                if (RecompVersion.TryParse(line, out var version))
                    versionText = version.ToString();
            },
            cancellationToken
        );

        return runResult.IsSuccess && runResult.Value == 0 ? versionText : null;
    }

    public async Task<WheelWizardStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = ReadInstalledState();
        var hasInstalledHost = fileSystem.File.Exists(environment.InstalledSetupFilePath);
        if (hasInstalledHost && !IsCurrentInstallState(state))
            return IsGameFileConfigured() ? WheelWizardStatus.OutOfDate : WheelWizardStatus.ConfigNotFinished;

        var installedVersion = IsCurrentInstallState(state) ? state!.SetupVersion : null;
        var latestRelease = await TryGetLatestReleaseAsync(cancellationToken);
        var setupUpgradeRequired =
            RecompVersion.TryParse(installedVersion, out var installed)
            && latestRelease is not null
            && latestRelease.Version.ComparePrecedenceTo(installed) > 0;
        var products =
            installedVersion is not null && hasInstalledHost && !setupUpgradeRequired ? await CheckProductsAsync(cancellationToken) : null;

        // Only a check that actually ran and failed is worth explaining, so a healthy status read
        // never opens the backend's lock file at all. A check refused by this service's own gate is
        // busy by definition: probing the backend lock instead would race the gaps between the
        // launch flow's setup invocations and misreport a launch in progress as out of date.
        var refusedByOwnOperation = products is { IsFailure: true } && products.Error.Message == OperationAlreadyRunningMessage;
        var installationBusy = products is { IsFailure: true } && (refusedByOwnOperation || IsInstallationBusy());
        if (installationBusy)
            logger.LogInformation("The WiiCompiled product check could not run because the installation is busy");

        var status = RecompStatusResolver.Resolve(
            IsGameFileConfigured(),
            hasInstalledHost ? installedVersion : null,
            latestRelease?.TagName,
            products?.IsSuccess == true ? products.Value : null,
            installationBusy
        );

        // A build that skipped the payload is healthy as far as the host is concerned, but it cannot
        // play online. Once the payload service is back, that is an update worth offering.
        if (
            status is (WheelWizardStatus.Ready or WheelWizardStatus.NoServerButInstalled)
            && await RetroWfcUpgradeAvailableAsync(state, cancellationToken)
        )
            return WheelWizardStatus.OutOfDate;

        return status;
    }

    /// <summary>
    /// Whether the installed Retro Rewind product was built without a Retro-WFC payload while the payload
    /// service is reachable again. Only a skipped installation ever probes, so a normal one costs nothing.
    /// </summary>
    private async Task<bool> RetroWfcUpgradeAvailableAsync(RecompInstallState? state, CancellationToken cancellationToken) =>
        state is { IsRetroWfcPayloadSkipped: true } && await payloadProbe.IsReachableAsync(cancellationToken);

    /// <summary>
    /// Decides which payload option the next setup operation receives. The rules live in
    /// <see cref="RecompRetroWfcPayloadPolicy"/>; this only supplies the probe and the user's answer.
    /// </summary>
    private async Task<OperationResult<RecompRetroWfcPayloadMode>> ResolveRetroWfcPayloadModeAsync(
        RecompInstallState? state,
        Func<Task<bool>>? confirmOfflineInstall,
        CancellationToken cancellationToken
    )
    {
        var hasRetroRewindSource = !string.IsNullOrWhiteSpace(environment.RetroRewindFolderPath);
        var serviceReachable =
            !RecompRetroWfcPayloadPolicy.NeedsServiceProbe(state, hasRetroRewindSource)
            || await payloadProbe.IsReachableAsync(cancellationToken);

        switch (RecompRetroWfcPayloadPolicy.Decide(state, hasRetroRewindSource, serviceReachable))
        {
            case RecompRetroWfcPayloadDecision.Download:
                return Ok(RecompRetroWfcPayloadMode.Download);
            case RecompRetroWfcPayloadDecision.Skip:
                return Ok(RecompRetroWfcPayloadMode.Skip);
            default:
                if (confirmOfflineInstall is null || !await confirmOfflineInstall())
                    return Fail(RetroWfcUnavailableMessage);

                logger.LogInformation("The Retro-WFC payload service is unreachable; installing WiiCompiled without online play");
                return Ok(RecompRetroWfcPayloadMode.Skip);
        }
    }

    /// <summary>
    /// Whether the backend's own per-installation lock is currently held. WheelWizard's operation gate
    /// only covers this process, so this is the only way to see a play session owned by something else:
    /// another WheelWizard instance, a game started outside WheelWizard, or a previous WheelWizard run
    /// that died while the game it launched kept running.
    /// </summary>
    private bool IsInstallationBusy()
    {
        try
        {
            var installRoot = Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(environment.InstallFolderPath));
            var parentFolderPath = fileSystem.Directory.GetParent(installRoot)?.FullName;
            if (parentFolderPath is null || !fileSystem.Directory.Exists(parentFolderPath))
                return false;

            // The lock file outlives the operation that created it, so its presence proves nothing:
            // only whether it can still be opened exclusively does.
            return fileSystem.Directory.EnumerateFiles(parentFolderPath, OperationLockSearchPattern).Any(IsFileLockedByAnotherProcess);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not determine whether the WiiCompiled installation is busy");
            return false;
        }
    }

    private bool IsFileLockedByAnotherProcess(string filePath)
    {
        try
        {
            // Taking the lock the way the backend takes it is the test. The handle is released
            // immediately, and this only ever runs on a path where an operation has already failed,
            // so it cannot be what makes a healthy operation lose the lock.
            using var stream = fileSystem.File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (Exception exception)
        {
            // Anything else (the file vanished, ACLs) is not evidence of a live operation.
            logger.LogWarning(exception, "Could not probe the WiiCompiled operation lock at {Path}", filePath);
            return false;
        }
    }

    public async Task<OperationResult<RecompProductsEvent>> CheckProductsAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return Fail(OperationAlreadyRunningMessage);
        try
        {
            return await CheckProductsCoreAsync(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<OperationResult<RecompProductsEvent>> CheckProductsCoreAsync(CancellationToken cancellationToken)
    {
        if (!fileSystem.File.Exists(environment.InstalledSetupFilePath))
            return Fail("WiiCompiled is not installed yet.");

        var state = ReadCurrentInstallState();
        if (state is null)
            return Fail("WiiCompiled requires a current install-state.json for its fixed installation path.");

        var products = new EventHolder<RecompProductsEvent>();
        var runResult = await processRunner.RunAsync(
            environment.InstalledSetupFilePath,
            // Every retro operation passes the Retro Rewind source, so the check answers about the
            // installation that the launch will actually read from.
            RecompSetupCommandBuilder.BuildCheckProductsArguments(environment.InstallFolderPath, environment.RetroRewindFolderPath),
            environment.InstallFolderPath,
            line =>
            {
                if (RecompSetupOutputParser.Parse(line) is RecompProductsEvent productsEvent)
                    products.Value = productsEvent;
            },
            cancellationToken
        );

        if (runResult.IsFailure)
            return runResult.Error;

        // Exit 0 means nothing needs repairing and exit 2 means something does; both are real answers.
        if (runResult.Value is not (0 or 2))
        {
            // Exit 1 covers every way the check itself can fail, and by far the most common one is a
            // game the user still has open. Say that instead of quoting the exit code at them.
            return Fail(IsInstallationBusy() ? InstallationBusyMessage : $"The recomp product check exited with code {runResult.Value}.");
        }

        if (products.Value is null)
            return Fail("The recomp product check did not report any products.");
        if (!IsCurrentProductReport(products.Value, state))
            return Fail("The recomp product check did not report the current installation identity.");

        return Ok(products.Value);
    }

    public async Task<OperationResult> InstallAsync(
        IProgress<RecompInstallProgress>? progress = null,
        Func<Task<bool>>? confirmOfflineInstall = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return Fail(OperationAlreadyRunningMessage);

        try
        {
            return await InstallCoreAsync(progress, confirmOfflineInstall, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<OperationResult> InstallCoreAsync(
        IProgress<RecompInstallProgress>? progress,
        Func<Task<bool>>? confirmOfflineInstall,
        CancellationToken cancellationToken
    )
    {
        // No platform guard here on purpose: AddRecomp() is the single gate, so this service only ever
        // exists on Windows in the first place.
        if (!IsGameFileConfigured())
            return Fail(t("message_warning.not_find_game.extra"));

        Report(progress, t("progress.recomp_checking_release"), 0);
        var state = ReadInstalledState();
        var installedVersion = string.IsNullOrWhiteSpace(state?.SetupVersion) ? null : state.SetupVersion;
        var hasInstalledHost = fileSystem.File.Exists(environment.InstalledSetupFilePath);
        var release = await TryGetLatestReleaseAsync(cancellationToken);

        // Decided up front, before any download or build, so the user is asked while nothing has started
        // yet rather than after a multi-minute build has already failed on the payload.
        var payloadModeResult = await ResolveRetroWfcPayloadModeAsync(state, confirmOfflineInstall, cancellationToken);
        if (payloadModeResult.IsFailure)
            return payloadModeResult.Error;
        var payloadMode = payloadModeResult.Value;

        // A skipped installation that can download again is an update in its own right: the host only
        // rebuilds when told the payload choice changed, and --check-products alone never says so.
        var forceRetroRebuild = state is { IsRetroWfcPayloadSkipped: true } && payloadMode == RecompRetroWfcPayloadMode.Download;

        // A repair is deliberately independent of GitHub: once a setup host has been installed,
        // it owns the installed toolkit/workspace and can bring changed compile inputs current offline.
        if (release is null)
        {
            if (hasInstalledHost && await InstalledHostCanRepairAsync(installedVersion, cancellationToken))
                return await RepairWhatTheCheckDemandsAsync(progress, payloadMode, forceRetroRebuild, cancellationToken);

            return Fail("Could not verify a current WiiCompiled setup release or installed repair host.");
        }

        if (hasInstalledHost && await InstalledHostCanRepairAsync(release.TagName, cancellationToken))
            return await RepairWhatTheCheckDemandsAsync(progress, payloadMode, forceRetroRebuild, cancellationToken);

        var setupResult = await EnsureSetupDownloadedAsync(release, progress, cancellationToken);
        if (setupResult.IsFailure)
            return setupResult.Error;

        return await RunSilentInstallAsync(setupResult.Value, payloadMode, progress, cancellationToken);
    }

    public async Task<OperationResult> ReconcileForLaunchAsync(
        IProgress<RecompInstallProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return Fail(OperationAlreadyRunningMessage);

        try
        {
            return await ReconcileForLaunchCoreAsync(progress, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<OperationResult> ReconcileForLaunchCoreAsync(
        IProgress<RecompInstallProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        _launchReconciled = false;
        if (!fileSystem.File.Exists(environment.InstalledSetupFilePath))
            return Fail("WiiCompiled is not installed yet.");

        var state = ReadCurrentInstallState();
        if (state is null)
            return Fail("WiiCompiled requires a current install-state.json for its fixed installation path.");
        if (!await SetupMatchesVersionAsync(environment.InstalledSetupFilePath, state.SetupVersion, cancellationToken))
            return Fail("The installed WiiCompiled host does not match its current install state.");

        // A launch never asks about offline play and never forces the payload upgrade: the user pressed
        // Play, not Update. A repair the check demands anyway still gains the payload when it is reachable.
        var payloadModeResult = await ResolveRetroWfcPayloadModeAsync(state, confirmOfflineInstall: null, cancellationToken);
        if (payloadModeResult.IsFailure)
            return payloadModeResult.Error;

        var repairResult = await RepairWhatTheCheckDemandsCoreAsync(
            progress,
            payloadModeResult.Value,
            forceRetroRebuild: false,
            reportCompletion: false,
            cancellationToken
        );
        if (repairResult.IsFailure)
            return repairResult;

        cancellationToken.ThrowIfCancellationRequested();
        var confirmedState = ReadCurrentInstallState();
        if (
            confirmedState is null
            || !VersionsMatch(confirmedState.SetupVersion, state.SetupVersion)
            || !fileSystem.File.Exists(environment.InstalledSetupFilePath)
        )
            return Fail("The pre-launch WiiCompiled reconciliation did not confirm the current installation contract.");

        _launchReconciled = true;
        Report(progress, t("progress.recomp_finished"), 100);
        return Ok();
    }

    public async Task<OperationResult> LaunchAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return Fail(OperationAlreadyRunningMessage);

        try
        {
            return await LaunchCoreAsync(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<OperationResult> LaunchCoreAsync(CancellationToken cancellationToken)
    {
        // The authorization is single use: it is consumed here whether or not the rest succeeds, so a
        // second launch always has to go through pre-launch reconciliation again.
        var reconciled = _launchReconciled;
        _launchReconciled = false;

        if (!fileSystem.File.Exists(environment.InstalledSetupFilePath))
            return Fail("WiiCompiled is not installed yet.");
        if (ReadCurrentInstallState() is null)
            return Fail("WiiCompiled requires a current install-state.json before it can launch.");
        if (!reconciled)
            return Fail("WiiCompiled must complete its current pre-launch reconciliation before it can launch.");

        var launchResult = await processRunner.RunAsync(
            environment.InstalledSetupFilePath,
            RecompSetupCommandBuilder.BuildLaunchArguments(retroRewind: true),
            environment.InstallFolderPath,
            onStandardOutputLine: null,
            cancellationToken
        );
        if (launchResult.IsFailure)
            return launchResult.Error;
        return launchResult.Value == 0 ? Ok() : Fail($"WiiCompiled exited with code {launchResult.Value}.");
    }

    public async Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return Fail(OperationAlreadyRunningMessage);

        try
        {
            return await Task.Run(UninstallCore, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private OperationResult UninstallCore()
    {
        _launchReconciled = false;

        // Only the recomp's own directories are removed. The shared Retro Rewind installation lives
        // outside them and survives on purpose: it belongs to WheelWizard's Dolphin frontend just as much.
        return TryCatch(
            () =>
            {
                DeleteFolderIfPresent(environment.InstallFolderPath);
                DeleteFolderIfPresent(environment.UserDataFolderPath);
                DeleteFolderIfPresent(environment.CacheFolderPath);
                DeleteFolderIfPresent(environment.NandCopyFolderPath);
                if (fileSystem.File.Exists(environment.PortableMarkerFilePath))
                    fileSystem.File.Delete(environment.PortableMarkerFilePath);
                logger.LogInformation("Uninstalled WiiCompiled from {InstallFolder}", environment.InstallFolderPath);
            },
            errorMessage: "Could not remove the WiiCompiled installation."
        );
    }

    private void DeleteFolderIfPresent(string folderPath)
    {
        if (!string.IsNullOrWhiteSpace(folderPath) && fileSystem.Directory.Exists(folderPath))
            fileSystem.Directory.Delete(folderPath, recursive: true);
    }

    private async Task<OperationResult<string>> EnsureSetupDownloadedAsync(
        RecompRelease release,
        IProgress<RecompInstallProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var cachedSetupPath = fileSystem.Path.Combine(environment.CacheFolderPath, BuildCachedSetupFileName(release.TagName));
        if (IsUsableFile(cachedSetupPath) && await SetupMatchesVersionAsync(cachedSetupPath, release.TagName, cancellationToken))
        {
            PruneCachedSetupsExcept(cachedSetupPath);
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

        if (!await SetupMatchesVersionAsync(cachedSetupPath, release.TagName, cancellationToken))
        {
            var removed = DeleteInvalidSetup(cachedSetupPath);
            return removed
                ? Fail($"The downloaded WiiCompiled setup did not report release {release.TagName}.")
                : Fail($"The downloaded WiiCompiled setup did not report release {release.TagName} and could not be removed.");
        }

        PruneCachedSetupsExcept(cachedSetupPath);
        return Ok(cachedSetupPath);
    }

    /// <summary>
    /// Drops setup executables cached for other releases. Each one is around 380 MB and the cache is
    /// only ever read for the release currently being installed, so keeping them meant every update
    /// permanently cost the user another installer's worth of disk. Failure here is deliberately
    /// silent: it is disk hygiene, never a reason to fail an install that has already succeeded.
    /// </summary>
    private void PruneCachedSetupsExcept(string keepFilePath)
    {
        try
        {
            if (!fileSystem.Directory.Exists(environment.CacheFolderPath))
                return;
            foreach (var candidate in fileSystem.Directory.EnumerateFiles(environment.CacheFolderPath, "WiiCompiled-Setup-*.exe"))
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

    private async Task<bool> InstalledHostCanRepairAsync(string? expectedVersion, CancellationToken cancellationToken)
    {
        if (!fileSystem.File.Exists(environment.InstalledSetupFilePath))
            return false;

        var state = ReadCurrentInstallState();
        // State schema is the explicit protocol boundary. Do not capability-probe an unknown host:
        // an old or corrupt state instead receives the verified release setup (or fails offline),
        // which is the only host allowed to upgrade the toolkit.
        if (state is null || !VersionsMatch(state.SetupVersion, expectedVersion))
            return false;

        // The state was already confirmed equal to the expected release, so this one version
        // invocation proves the installed host matches both authoritative version values.
        return await SetupMatchesVersionAsync(environment.InstalledSetupFilePath, state.SetupVersion, cancellationToken);
    }

    private async Task<bool> SetupMatchesVersionAsync(string setupFilePath, string? expectedVersion, CancellationToken cancellationToken)
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

    private bool VersionsMatch(string? first, string? second) =>
        RecompVersion.TryParse(first, out var parsedFirst)
        && RecompVersion.TryParse(second, out var parsedSecond)
        && parsedFirst.ComparePrecedenceTo(parsedSecond) == 0;

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

    private async Task<OperationResult> RunSilentInstallAsync(
        string setupFilePath,
        RecompRetroWfcPayloadMode payloadMode,
        IProgress<RecompInstallProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var request = new RecompInstallRequest
        {
            GameFilePath = environment.GameFilePath,
            InstallFolderPath = environment.InstallFolderPath,
            RetroRewindFolderPath = environment.RetroRewindFolderPath,
            Portable = environment.IsPortableInstall,
            RetroWfcPayloadMode = payloadMode,
        };

        var arguments = RecompSetupCommandBuilder.BuildSilentInstallArguments(request);
        logger.LogInformation("Running the recomp setup: {Setup} {Arguments}", setupFilePath, arguments);

        Report(progress, t("progress.recomp_running_setup"), SetupPercentFloor);

        // The install directory must not be the setup's working directory, and must not be
        // pre-created here: the setup publishes by renaming the install directory itself, and a
        // CWD handle on it makes that rename fail with a sharing violation after the full build.
        var resultHolder = new EventHolder<RecompSetupResultEvent>();
        var runResult = await processRunner.RunAsync(
            setupFilePath,
            arguments,
            workingDirectory: null,
            line => HandleSetupOutput(line, progress, resultHolder),
            cancellationToken
        );

        return FinishSetupRun(runResult, resultHolder, progress);
    }

    /// <summary>
    /// Contract steps 2-4: ask the installed host what is stale, repair only when it says so, then
    /// confirm the repair produced a healthy installation. An asset-only Retro Rewind change reports
    /// <c>current</c>, so the expensive half never runs.
    /// </summary>
    private async Task<OperationResult> RepairWhatTheCheckDemandsAsync(
        IProgress<RecompInstallProgress>? progress,
        RecompRetroWfcPayloadMode payloadMode,
        bool forceRetroRebuild,
        CancellationToken cancellationToken
    ) => await RepairWhatTheCheckDemandsCoreAsync(progress, payloadMode, forceRetroRebuild, reportCompletion: true, cancellationToken);

    /// <param name="forceRetroRebuild">
    /// Runs the repair even when the check reports every product current. The host decides for itself
    /// what that repair rebuilds; this exists so a payload choice that changed, which the check does
    /// not see, still reaches it.
    /// </param>
    private async Task<OperationResult> RepairWhatTheCheckDemandsCoreAsync(
        IProgress<RecompInstallProgress>? progress,
        RecompRetroWfcPayloadMode payloadMode,
        bool forceRetroRebuild,
        bool reportCompletion,
        CancellationToken cancellationToken
    )
    {
        var checkResult = await CheckProductsCoreAsync(cancellationToken);
        if (checkResult.IsFailure)
            return checkResult.Error;

        if (!NeedsRepair(checkResult.Value) && !forceRetroRebuild)
        {
            if (reportCompletion)
                Report(progress, t("progress.recomp_finished"), 100);
            return Ok();
        }

        logger.LogInformation(
            "Repairing WiiCompiled products (base: {BaseStatus}, retro: {RetroStatus}, compile required: {CompileRequired}, payload: {PayloadMode})",
            checkResult.Value.Base.State,
            checkResult.Value.RetroRewind.State,
            checkResult.Value.Base.RequiresCompile || checkResult.Value.RetroRewind.RequiresCompile,
            payloadMode
        );

        var repairResult = await RunTargetedRepairAsync(progress, payloadMode, cancellationToken);
        if (repairResult.IsFailure)
            return repairResult;

        // Recheck health after the terminal success result: the backend, not this client, decides
        // whether the repair actually brought every product current.
        var recheckResult = await CheckProductsCoreAsync(cancellationToken);
        if (recheckResult.IsFailure)
            return recheckResult.Error;
        if (NeedsRepair(recheckResult.Value))
            return Fail(BuildUnrepairedProductsMessage(recheckResult.Value));

        if (reportCompletion)
            Report(progress, t("progress.recomp_finished"), 100);
        return Ok();
    }

    private async Task<OperationResult> RunTargetedRepairAsync(
        IProgress<RecompInstallProgress>? progress,
        RecompRetroWfcPayloadMode payloadMode,
        CancellationToken cancellationToken
    )
    {
        // The launcher brings Retro Rewind current before any setup operation, so the source the
        // backend snapshots its compile inputs from is the state the launch will actually use.
        var retroRewindFolderPath = environment.RetroRewindFolderPath;
        if (string.IsNullOrWhiteSpace(retroRewindFolderPath))
            return Fail("Retro Rewind must be installed before WiiCompiled can repair or launch.");

        var arguments = RecompSetupCommandBuilder.BuildRepairProductsArguments(
            environment.InstallFolderPath,
            retroRewindFolderPath,
            payloadMode
        );

        Report(progress, t("progress.recomp_running_setup"), SetupPercentFloor);
        var resultHolder = new EventHolder<RecompSetupResultEvent>();
        OperationResult<int> runResult;
        try
        {
            runResult = await processRunner.RunAsync(
                environment.InstalledSetupFilePath,
                arguments,
                environment.InstallFolderPath,
                line => HandleSetupOutput(line, progress, resultHolder),
                cancellationToken
            );
        }
        finally
        {
            // The backend may have changed installed product bytes even when it later reports a
            // failure or observes cancellation, so any earlier launch authorization is void.
            _launchReconciled = false;
        }

        return FinishSetupRun(runResult, resultHolder, progress, reportCompletion: false);
    }

    /// <summary>
    /// Whether the backend has work to do. `absent` normally requires nothing, but WheelWizard is the
    /// Retro Rewind frontend: when a Retro Rewind source is present, a missing Retro Rewind product is
    /// something the repair must actually install, not a state to report as healthy forever.
    /// </summary>
    private bool NeedsRepair(RecompProductsEvent products) =>
        products.ActionRequired
        || (products.RetroRewind.State == RecompProductState.Absent && !string.IsNullOrWhiteSpace(environment.RetroRewindFolderPath));

    private static string BuildUnrepairedProductsMessage(RecompProductsEvent products)
    {
        var detail = products.RetroRewind.ActionRequired ? products.RetroRewind.Detail : products.Base.Detail;
        return string.IsNullOrWhiteSpace(detail)
            ? "The WiiCompiled repair finished, but the installed products are still not current."
            : $"The WiiCompiled repair finished, but the installed products are still not current: {detail}";
    }

    private void HandleSetupOutput(
        string line,
        IProgress<RecompInstallProgress>? progress,
        EventHolder<RecompSetupResultEvent> resultHolder
    )
    {
        switch (RecompSetupOutputParser.Parse(line))
        {
            case RecompSetupProgressEvent progressEvent:
                Report(
                    progress,
                    string.IsNullOrWhiteSpace(progressEvent.Message) ? progressEvent.Stage : progressEvent.Message,
                    SetupPercentFloor + (progressEvent.Percent * (100 - SetupPercentFloor) / 100)
                );
                break;
            case RecompSetupResultEvent resultEvent:
                resultHolder.Value = resultEvent;
                break;
        }
    }

    private OperationResult FinishSetupRun(
        OperationResult<int> runResult,
        EventHolder<RecompSetupResultEvent> resultHolder,
        IProgress<RecompInstallProgress>? progress,
        bool reportCompletion = true
    )
    {
        if (runResult.IsFailure)
            return runResult.Error;

        var setupResult = resultHolder.Value;
        if (setupResult is { Success: false })
            return Fail(string.IsNullOrWhiteSpace(setupResult.Error) ? "The recomp installer reported a failure." : setupResult.Error);

        if (runResult.Value != 0)
            return Fail($"The recomp installer exited with code {runResult.Value}.");

        // Every operation emits exactly one terminal result record.
        if (setupResult is null || resultHolder.Count != 1)
            return Fail("The recomp installer did not report exactly one terminal result.");

        _launchReconciled = false;
        if (reportCompletion)
            Report(progress, t("progress.recomp_finished"), 100);
        return Ok();
    }

    private async Task<RecompRelease?> TryGetLatestReleaseAsync(CancellationToken cancellationToken)
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

    private RecompInstallState? ReadInstalledState()
    {
        try
        {
            if (!fileSystem.File.Exists(environment.InstallStateFilePath))
                return null;

            var json = fileSystem.File.ReadAllText(environment.InstallStateFilePath);
            return JsonSerializer.Deserialize<RecompInstallState>(json, InstallStateJsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to read the recomp install state");
            return null;
        }
    }

    private RecompInstallState? ReadCurrentInstallState()
    {
        var state = ReadInstalledState();
        return IsCurrentInstallState(state) ? state : null;
    }

    private bool IsCurrentInstallState(RecompInstallState? state) =>
        state is { SchemaVersion: CurrentInstallStateSchemaVersion }
        && RecompVersion.TryParse(state.SetupVersion, out _)
        && PathsMatch(state.InstallDir, environment.InstallFolderPath);

    private bool IsCurrentProductReport(RecompProductsEvent products, RecompInstallState state) =>
        products.ProtocolValid
        && products.Base.ProtocolValid
        && products.RetroRewind.ProtocolValid
        && VersionsMatch(products.SetupVersion, state.SetupVersion)
        && PathsMatch(products.InstallDir, environment.InstallFolderPath);

    private bool PathsMatch(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        try
        {
            var normalizedFirst = Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(first));
            var normalizedSecond = Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(second));
            return normalizedFirst.Equals(normalizedSecond, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not compare recomp installation paths");
            return false;
        }
    }

    private bool IsGameFileConfigured()
    {
        var gameFilePath = environment.GameFilePath;
        return !string.IsNullOrWhiteSpace(gameFilePath) && fileSystem.File.Exists(gameFilePath);
    }

    private bool IsUsableFile(string filePath)
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

    private static string BuildCachedSetupFileName(string tagName)
    {
        var sanitized = new string(tagName.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        return $"WiiCompiled-Setup-{sanitized}.exe";
    }

    private static void Report(IProgress<RecompInstallProgress>? progress, string message, int percent) =>
        progress?.Report(new(message, Math.Clamp(percent, 0, 100)));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // The gate is deliberately left undisposed: an in-flight operation still has to Release() it,
        // and a SemaphoreSlim whose wait handle was never touched holds no OS resources anyway.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Forwards progress synchronously, so the single <see cref="Progress{T}"/> the launcher owns stays the
    /// only place where marshalling to the UI thread happens.
    /// </summary>
    private sealed class DelegateProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>
    /// NDJSON lines arrive on a process output thread, so publish the ones we keep with a memory barrier.
    /// </summary>
    private sealed class EventHolder<T>
        where T : class
    {
        private T? _value;

        public T? Value
        {
            get => Volatile.Read(ref _value);
            set
            {
                Volatile.Write(ref _value, value);
                Interlocked.Increment(ref _count);
            }
        }

        private int _count;
        public int Count => Volatile.Read(ref _count);
    }
}
