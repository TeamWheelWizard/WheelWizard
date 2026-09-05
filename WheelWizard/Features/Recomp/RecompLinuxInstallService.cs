using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.Models.Enums;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// The Linux implementation of <see cref="IRecompInstallService"/>, driving the recomp's AppImage.
/// <para>
/// The AppImage does not speak the Windows setup's contract: it takes subcommands, installs into its own
/// XDG data directory, keeps an <c>install-state.json</c> of its own shape that never records the setup
/// version, and only prints a human-readable product table. So this service keeps the pieces the
/// AppImage does not provide itself: it copies the downloaded AppImage beside a
/// <see cref="RecompInstallState"/> it writes (the "installed host"), and it reconstructs the product
/// report from the files the AppImage leaves behind (<see cref="RecompLinuxProductInspector"/>).
/// </para>
/// <para>
/// A repair is <c>install</c> without <c>--game</c>: the AppImage then reuses the disc assets it already
/// extracted and its own translation cache, so only what actually changed (typically a new Code.pul)
/// is retranslated and recompiled. A new setup release is a full <c>install --game</c> with the new
/// AppImage, exactly like a fresh install.
/// </para>
/// </summary>
public sealed class RecompLinuxInstallService : IRecompInstallService
{
    // Where the setup phase starts on the 0-100 progress bar; the download phase before it lives in the acquirer.
    private const int SetupPercentFloor = RecompSetupHostAcquirer.SetupPercentFloor;

    private const int CurrentInstallStateSchemaVersion = 1;

    // A launch holds the gate for the whole play session, so "busy" almost always means "the game is running".
    private const string OperationAlreadyRunningMessage = "Another WiiCompiled operation is already running.";

    private const string NotInstalledMessage = "WiiCompiled is not installed yet.";
    private const string StaleStateMessage = "WiiCompiled requires a current install-state.json before it can continue.";

    private const string RetroWfcUnavailableMessage =
        "The Retro WFC servers are not responding, so WiiCompiled cannot set up online play right now. Try again later, or install without online play.";

    private static readonly JsonSerializerOptions InstallStateJsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly IRecompEnvironment environment;
    private readonly IRecompProcessRunner processRunner;
    private readonly RecompSetupHostAcquirer hosts;
    private readonly RecompLinuxProductInspector inspector;
    private readonly IRecompRetroWfcPayloadProbe payloadProbe;
    private readonly IFileSystem fileSystem;
    private readonly ILogger<RecompLinuxInstallService> logger;

    // Set by a successful pre-launch reconciliation and consumed by the launch that follows it.
    // Both run under _operationGate, which is also what keeps the handoff a single-use token.
    private bool _launchReconciled;
    private int _disposed;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public RecompLinuxInstallService(
        IRecompEnvironment environment,
        IRecompProcessRunner processRunner,
        RecompSetupHostAcquirer hosts,
        RecompLinuxProductInspector inspector,
        IRecompRetroWfcPayloadProbe payloadProbe,
        IFileSystem fileSystem,
        ILogger<RecompLinuxInstallService> logger
    )
    {
        this.environment = environment;
        this.processRunner = processRunner;
        this.hosts = hosts;
        this.inspector = inspector;
        this.payloadProbe = payloadProbe;
        this.fileSystem = fileSystem;
        this.logger = logger;
    }

    public bool OperationInFlight => _operationGate.CurrentCount == 0;

    public bool IsInstalled => HasInstalledHost && ReadCurrentInstallState() is not null;

    private bool HasInstalledHost => fileSystem.File.Exists(environment.InstalledSetupFilePath);

    public async Task<WheelWizardStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = ReadInstalledState();
        var hasInstalledHost = HasInstalledHost;
        if (hasInstalledHost && !IsCurrentInstallState(state))
            return IsGameFileConfigured() ? WheelWizardStatus.OutOfDate : WheelWizardStatus.ConfigNotFinished;

        var installedVersion = IsCurrentInstallState(state) ? state!.SetupVersion : null;
        var latestRelease = await hosts.TryGetLatestReleaseAsync(cancellationToken);
        var setupUpgradeRequired =
            RecompVersion.TryParse(installedVersion, out var installed)
            && latestRelease is not null
            && latestRelease.Version.ComparePrecedenceTo(installed) > 0;
        var products =
            installedVersion is not null && hasInstalledHost && !setupUpgradeRequired ? await CheckProductsAsync(cancellationToken) : null;

        // The inspection only reads files, so the one thing that can refuse it is this service's own gate:
        // an install, a reconciliation or a play session in progress. That is busy, not stale.
        var installationBusy = products is { IsFailure: true } && products.Error.Message == OperationAlreadyRunningMessage;

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
            && state is { IsRetroWfcPayloadSkipped: true }
            && await payloadProbe.IsReachableAsync(cancellationToken)
        )
            return WheelWizardStatus.OutOfDate;

        return status;
    }

    public async Task<OperationResult<RecompProductsEvent>> CheckProductsAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return Fail(OperationAlreadyRunningMessage);
        try
        {
            return CheckProductsCore();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private OperationResult<RecompProductsEvent> CheckProductsCore()
    {
        if (!HasInstalledHost)
            return Fail(NotInstalledMessage);

        var state = ReadCurrentInstallState();
        if (state is null)
            return Fail(StaleStateMessage);

        return Ok(
            inspector.Inspect(environment.BackendStateFilePath, state.SetupVersion, environment.InstallFolderPath, environment.RetroRewindFolderPath)
        );
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
        if (!IsGameFileConfigured())
            return Fail(t("message_warning.not_find_game.extra"));

        Report(progress, t("progress.recomp_checking_release"), 0);
        var state = ReadInstalledState();
        var release = await hosts.TryGetLatestReleaseAsync(cancellationToken);

        // Decided up front, before any download or build, so the user is asked while nothing has started
        // yet rather than after a multi-minute build has already failed on the payload.
        var payloadModeResult = await ResolveRetroWfcPayloadModeAsync(state, confirmOfflineInstall, cancellationToken);
        if (payloadModeResult.IsFailure)
            return payloadModeResult.Error;
        var payloadMode = payloadModeResult.Value;

        // A skipped installation that can download again is an update in its own right: the inspection
        // cannot see the payload choice, so the rebuild has to be forced.
        var forceRetroRebuild = state is { IsRetroWfcPayloadSkipped: true } && payloadMode == RecompRetroWfcPayloadMode.Download;

        // The installed host repairs on its own, offline included, as long as no newer release exists.
        if (await InstalledHostIsCurrentAsync(state, release, cancellationToken))
            return await RepairWhatTheCheckDemandsAsync(progress, payloadMode, forceRetroRebuild, reportCompletion: true, cancellationToken);

        if (release is null)
            return Fail("Could not verify a current WiiCompiled setup release or installed repair host.");

        var setupResult = await hosts.EnsureSetupDownloadedAsync(release, environment.CacheFolderPath, progress, cancellationToken);
        if (setupResult.IsFailure)
            return setupResult.Error;

        return await RunFullInstallAsync(setupResult.Value, release, payloadMode, progress, cancellationToken);
    }

    /// <summary>
    /// Whether the installed AppImage is the release to keep using: its state is current, it is the newest
    /// release (or GitHub is unreachable), and the file itself still reports that version.
    /// </summary>
    private async Task<bool> InstalledHostIsCurrentAsync(RecompInstallState? state, RecompRelease? release, CancellationToken cancellationToken)
    {
        if (!HasInstalledHost || !IsCurrentInstallState(state))
            return false;
        if (release is not null && !RecompSetupHostAcquirer.VersionsMatch(state!.SetupVersion, release.TagName))
            return false;

        return await hosts.SetupMatchesVersionAsync(environment.InstalledSetupFilePath, state!.SetupVersion, cancellationToken);
    }

    private async Task<OperationResult> RunFullInstallAsync(
        string setupFilePath,
        RecompRelease release,
        RecompRetroWfcPayloadMode payloadMode,
        IProgress<RecompInstallProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var arguments = RecompLinuxSetupCommandBuilder.BuildInstallArguments(
            environment.GameFilePath,
            environment.RetroRewindFolderPath,
            payloadMode
        );
        logger.LogInformation("Running the recomp setup: {Setup} {Arguments}", setupFilePath, arguments);
        Report(progress, t("progress.recomp_running_setup"), SetupPercentFloor);

        var resultHolder = new EventHolder<RecompSetupResultEvent>();
        OperationResult<int> runResult;
        try
        {
            runResult = await processRunner.RunAsync(
                setupFilePath,
                arguments,
                workingDirectory: null,
                line => HandleSetupOutput(line, progress, resultHolder),
                cancellationToken
            );
        }
        finally
        {
            _launchReconciled = false;
        }

        var setupOutcome = FinishSetupRun(runResult, resultHolder);
        if (setupOutcome.IsFailure)
            return setupOutcome;

        // The AppImage reports the version it actually is; the release tag is only the fallback.
        var installedVersion = RecompVersion.TryParse(resultHolder.Value?.Version, out var reported) ? reported.ToString() : release.Version.ToString();
        var recordResult = RecordInstalledHost(setupFilePath, installedVersion, payloadMode);
        if (recordResult.IsFailure)
            return recordResult;

        Report(progress, t("progress.recomp_finished"), 100);
        return Ok();
    }

    /// <summary>
    /// Makes the AppImage that just installed the game the installed host: a copy beside a state file
    /// that says which release it is. Only after this does the installation count as present.
    /// </summary>
    private OperationResult RecordInstalledHost(string setupFilePath, string installedVersion, RecompRetroWfcPayloadMode payloadMode)
    {
        return TryCatch(
            () =>
            {
                var hostFolderPath = fileSystem.Path.GetDirectoryName(environment.InstalledSetupFilePath);
                if (!string.IsNullOrWhiteSpace(hostFolderPath))
                    fileSystem.Directory.CreateDirectory(hostFolderPath);
                if (!PathsMatch(setupFilePath, environment.InstalledSetupFilePath))
                    fileSystem.File.Copy(setupFilePath, environment.InstalledSetupFilePath, overwrite: true);
                hosts.MakeExecutable(environment.InstalledSetupFilePath);

                var retroRewindInstalled = environment.RetroRewindFolderPath is not null;
                WriteInstallState(
                    new()
                    {
                        SchemaVersion = CurrentInstallStateSchemaVersion,
                        SetupVersion = installedVersion,
                        InstallDir = environment.InstallFolderPath,
                        RetroRewindInstalled = retroRewindInstalled,
                        RetroWfcPayloadMode = retroRewindInstalled ? PayloadModeName(payloadMode) : string.Empty,
                    }
                );
            },
            errorMessage: "WiiCompiled was built, but Wheel Wizard could not record the installation."
        );
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
        if (!HasInstalledHost)
            return Fail(NotInstalledMessage);

        var state = ReadCurrentInstallState();
        if (state is null)
            return Fail(StaleStateMessage);
        if (!await hosts.SetupMatchesVersionAsync(environment.InstalledSetupFilePath, state.SetupVersion, cancellationToken))
            return Fail("The installed WiiCompiled host does not match its current install state.");

        // A launch never asks about offline play and never forces the payload upgrade: the user pressed
        // Play, not Update. A repair the check demands anyway still gains the payload when it is reachable.
        var payloadModeResult = await ResolveRetroWfcPayloadModeAsync(state, confirmOfflineInstall: null, cancellationToken);
        if (payloadModeResult.IsFailure)
            return payloadModeResult.Error;

        var repairResult = await RepairWhatTheCheckDemandsAsync(
            progress,
            payloadModeResult.Value,
            forceRetroRebuild: false,
            reportCompletion: false,
            cancellationToken
        );
        if (repairResult.IsFailure)
            return repairResult;

        cancellationToken.ThrowIfCancellationRequested();
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

        if (!HasInstalledHost)
            return Fail(NotInstalledMessage);
        if (ReadCurrentInstallState() is null)
            return Fail(StaleStateMessage);
        if (!reconciled)
            return Fail("WiiCompiled must complete its current pre-launch reconciliation before it can launch.");

        // The AppImage starts the product from the product's own directory and waits for it to exit.
        var launchResult = await processRunner.RunAsync(
            environment.InstalledSetupFilePath,
            RecompLinuxSetupCommandBuilder.BuildLaunchArguments(retroRewind: true),
            workingDirectory: null,
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
            return await UninstallCoreAsync(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<OperationResult> UninstallCoreAsync(CancellationToken cancellationToken)
    {
        _launchReconciled = false;

        // Let the AppImage remove what it registered outside its folder (the application-menu entries)
        // and whatever it installed. Its folder is removed below either way, so a failure here is logged,
        // not fatal.
        if (HasInstalledHost)
        {
            var uninstallResult = await processRunner.RunAsync(
                environment.InstalledSetupFilePath,
                RecompLinuxSetupCommandBuilder.BuildUninstallArguments(),
                workingDirectory: null,
                onStandardOutputLine: null,
                cancellationToken
            );
            if (uninstallResult.IsFailure || uninstallResult.Value != 0)
                logger.LogWarning("The WiiCompiled AppImage did not uninstall cleanly; removing its folder anyway");
        }

        // The backend folder holds the products, the build workspace, Config.toml and the private NAND:
        // everything the Windows uninstall removes as Install and UserData. The shared Retro Rewind
        // installation lives elsewhere and survives on purpose.
        return TryCatch(
            () =>
            {
                DeleteFolderIfPresent(environment.InstallFolderPath);
                DeleteFolderIfPresent(fileSystem.Path.GetDirectoryName(environment.InstalledSetupFilePath));
                DeleteFolderIfPresent(environment.CacheFolderPath);
                DeleteFolderIfPresent(environment.NandCopyFolderPath);
                logger.LogInformation("Uninstalled WiiCompiled from {InstallFolder}", environment.InstallFolderPath);
            },
            errorMessage: "Could not remove the WiiCompiled installation."
        );
    }

    private void DeleteFolderIfPresent(string? folderPath)
    {
        if (!string.IsNullOrWhiteSpace(folderPath) && fileSystem.Directory.Exists(folderPath))
            fileSystem.Directory.Delete(folderPath, recursive: true);
    }

    /// <summary>
    /// Asks the inspector what is stale, rebuilds only when it says so, then confirms the rebuild
    /// produced healthy products. An asset-only Retro Rewind change reports <c>current</c>, so the
    /// expensive half never runs.
    /// </summary>
    /// <param name="forceRetroRebuild">
    /// Runs the rebuild even when every product is current, so a payload choice that changed, which
    /// the inspection cannot see, still reaches the AppImage.
    /// </param>
    private async Task<OperationResult> RepairWhatTheCheckDemandsAsync(
        IProgress<RecompInstallProgress>? progress,
        RecompRetroWfcPayloadMode payloadMode,
        bool forceRetroRebuild,
        bool reportCompletion,
        CancellationToken cancellationToken
    )
    {
        var checkResult = CheckProductsCore();
        if (checkResult.IsFailure)
            return checkResult.Error;

        if (!NeedsRepair(checkResult.Value) && !forceRetroRebuild)
        {
            if (reportCompletion)
                Report(progress, t("progress.recomp_finished"), 100);
            return Ok();
        }

        logger.LogInformation(
            "Rebuilding WiiCompiled products (base: {BaseStatus}, retro: {RetroStatus}, payload: {PayloadMode})",
            checkResult.Value.Base.State,
            checkResult.Value.RetroRewind.State,
            payloadMode
        );

        var repairResult = await RunIncrementalInstallAsync(progress, payloadMode, cancellationToken);
        if (repairResult.IsFailure)
            return repairResult;

        var recheckResult = CheckProductsCore();
        if (recheckResult.IsFailure)
            return recheckResult.Error;
        if (NeedsRepair(recheckResult.Value))
            return Fail(BuildUnrepairedProductsMessage(recheckResult.Value));

        if (reportCompletion)
            Report(progress, t("progress.recomp_finished"), 100);
        return Ok();
    }

    /// <summary>
    /// <c>install</c> without <c>--game</c>: the AppImage keeps the extracted disc and its translation
    /// cache, so this only redoes what a changed Code.pul or payload choice requires.
    /// </summary>
    private async Task<OperationResult> RunIncrementalInstallAsync(
        IProgress<RecompInstallProgress>? progress,
        RecompRetroWfcPayloadMode payloadMode,
        CancellationToken cancellationToken
    )
    {
        var retroRewindFolderPath = environment.RetroRewindFolderPath;
        if (string.IsNullOrWhiteSpace(retroRewindFolderPath))
            return Fail("Retro Rewind must be installed before WiiCompiled can repair or launch.");

        var arguments = RecompLinuxSetupCommandBuilder.BuildInstallArguments(gameFilePath: null, retroRewindFolderPath, payloadMode);
        logger.LogInformation("Running the recomp setup: {Setup} {Arguments}", environment.InstalledSetupFilePath, arguments);
        Report(progress, t("progress.recomp_running_setup"), SetupPercentFloor);

        var resultHolder = new EventHolder<RecompSetupResultEvent>();
        OperationResult<int> runResult;
        try
        {
            runResult = await processRunner.RunAsync(
                environment.InstalledSetupFilePath,
                arguments,
                workingDirectory: null,
                line => HandleSetupOutput(line, progress, resultHolder),
                cancellationToken
            );
        }
        finally
        {
            // The AppImage may have replaced product bytes even when it later reports a failure or
            // observes cancellation, so any earlier launch authorization is void.
            _launchReconciled = false;
        }

        var setupOutcome = FinishSetupRun(runResult, resultHolder);
        if (setupOutcome.IsFailure)
            return setupOutcome;

        // The product now embeds this payload choice; remember it so the next status read can tell
        // an offline-only build from a normal one. Losing this only costs a redundant rebuild later.
        try
        {
            var state = ReadInstalledState();
            if (state is not null)
            {
                state.RetroRewindInstalled = true;
                state.RetroWfcPayloadMode = PayloadModeName(payloadMode);
                WriteInstallState(state);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not record the Retro-WFC payload mode of the rebuilt WiiCompiled products");
        }

        return Ok();
    }

    /// <summary>
    /// Whether the AppImage has work to do. `absent` normally requires nothing, but WheelWizard is the
    /// Retro Rewind frontend: when a Retro Rewind source is present, a missing Retro Rewind product is
    /// something the rebuild must actually install, not a state to report as healthy forever.
    /// </summary>
    private bool NeedsRepair(RecompProductsEvent products) =>
        products.ActionRequired
        || (products.RetroRewind.State == RecompProductState.Absent && !string.IsNullOrWhiteSpace(environment.RetroRewindFolderPath));

    private static string BuildUnrepairedProductsMessage(RecompProductsEvent products)
    {
        var detail = products.RetroRewind.ActionRequired ? products.RetroRewind.Detail : products.Base.Detail;
        return string.IsNullOrWhiteSpace(detail)
            ? "The WiiCompiled rebuild finished, but the installed products are still not current."
            : $"The WiiCompiled rebuild finished, but the installed products are still not current: {detail}";
    }

    private static void HandleSetupOutput(
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

    private static OperationResult FinishSetupRun(OperationResult<int> runResult, EventHolder<RecompSetupResultEvent> resultHolder)
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

        return Ok();
    }

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

    // The same spellings the Windows setup writes, so RecompRetroWfcPayloadPolicy reads both alike.
    private static string PayloadModeName(RecompRetroWfcPayloadMode mode) => mode == RecompRetroWfcPayloadMode.Skip ? "skipped" : "downloaded";

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

    private void WriteInstallState(RecompInstallState state)
    {
        var folderPath = fileSystem.Path.GetDirectoryName(environment.InstallStateFilePath);
        if (!string.IsNullOrWhiteSpace(folderPath))
            fileSystem.Directory.CreateDirectory(folderPath);
        fileSystem.File.WriteAllText(environment.InstallStateFilePath, JsonSerializer.Serialize(state, InstallStateJsonOptions));
    }

    private RecompInstallState? ReadCurrentInstallState()
    {
        var state = ReadInstalledState();
        return IsCurrentInstallState(state) ? state : null;
    }

    // Wheel Wizard's state is only as good as the AppImage's own: a backend folder the user wiped by
    // hand leaves nothing to check or launch, so the installation reads as stale and the next Update
    // runs a full install again.
    private bool IsCurrentInstallState(RecompInstallState? state) =>
        state is { SchemaVersion: CurrentInstallStateSchemaVersion }
        && RecompVersion.TryParse(state.SetupVersion, out _)
        && PathsMatch(state.InstallDir, environment.InstallFolderPath)
        && fileSystem.File.Exists(environment.BackendStateFilePath);

    private bool PathsMatch(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        try
        {
            var normalizedFirst = Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(first));
            var normalizedSecond = Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(second));
            return normalizedFirst.Equals(normalizedSecond, StringComparison.Ordinal);
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

    private static void Report(IProgress<RecompInstallProgress>? progress, string message, int percent) =>
        RecompSetupHostAcquirer.Report(progress, message, percent);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // The gate is deliberately left undisposed: an in-flight operation still has to Release() it,
        // and a SemaphoreSlim whose wait handle was never touched holds no OS resources anyway.
        GC.SuppressFinalize(this);
    }
}
