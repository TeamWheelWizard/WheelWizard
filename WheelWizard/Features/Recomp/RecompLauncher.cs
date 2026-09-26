using WheelWizard.CustomDistributions;
using WheelWizard.Launching;
using WheelWizard.Models.Enums;
using WheelWizard.Mods;
using WheelWizard.Shared.MessageTranslations;

namespace WheelWizard.Recomp;

/// <summary>Coordinates distribution readiness, NAND choices and recomp setup through injected presentation.</summary>
public class RecompLauncher(
    IRecompInstallService installService,
    ICustomDistributionSingletonService customDistributions,
    IModsLaunchService modsLaunchService,
    IRecompDolphinDataService dolphinData,
    ICustomDistributionPaths distributionPaths,
    IModOperationPresentation modPresentation,
    IRecompPresentation presentation,
    ILaunchPrompts launchPrompts
) : ILauncher
{
    public string GameTitle { get; } = "WiiCompiled";

    public async Task<OperationResult> Launch()
    {
        try
        {
            var target = distributionPaths.PatchesFolderPath;
            var clear = modsLaunchService.ShouldAskToClearTargetFolder(target) && await launchPrompts.ConfirmPatchCleanupAsync();
            var modsResult = await modPresentation.RunAsync(
                (progress, _) => modsLaunchService.PrepareModsForLaunch(target, clear, progress)
            );
            if (modsResult.IsFailure)
                return modsResult;

            var preparation = await presentation.RunAsync(
                RecompOperationKind.PrepareLaunch,
                async operation =>
                {
                    var result = await installService.ReconcileForLaunchAsync(operation.InstallProgress, operation.CancellationToken);
                    if (operation.CancellationToken.IsCancellationRequested)
                        return CancellationWarning("WiiCompiled launch preparation was cancelled.");
                    return result;
                }
            );
            if (preparation.IsFailure)
                return preparation;

            // The presentation scope closes before launching; the game session is not cancellable.
            return await installService.LaunchAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return CancellationWarning("WiiCompiled launch preparation was cancelled.");
        }
    }

    public async Task<OperationResult> Install()
    {
        var nandChoice = await AskForDolphinNandChoiceAsync();
        if (nandChoice.IsFailure)
            return nandChoice;

        var installResult = await RunSetupAsync(RecompOperationKind.Install);
        if (installResult.IsFailure)
            return installResult;

        // The recomp compiled without any NAND knowledge; pointing its Config.toml at the chosen
        // Wii data is what makes the choice real, and it applies on the very next launch.
        return dolphinData.ApplyNandToRecompConfig();
    }

    /// <summary>
    /// The setup service selects its full-release or targeted-repair operation after Retro Rewind
    /// has been brought current below, and only after --check-products says work is needed.
    /// Re-applying the NAND setting afterwards keeps an installation in sync when the user moved or
    /// re-linked their Dolphin data since the last one.
    /// </summary>
    public async Task<OperationResult> Update()
    {
        var updateResult = await RunSetupAsync(RecompOperationKind.Update);
        if (updateResult.IsFailure)
            return updateResult;

        return dolphinData.ApplyNandToRecompConfig();
    }

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        // While a launch or setup operation is running, the session only got this far by being
        // ready
        if (installService.OperationInFlight)
            return WheelWizardStatus.Ready;

        try
        {
            var retroRewindStatus = await customDistributions.RetroRewind.GetCurrentStatusAsync();
            if (retroRewindStatus.IsFailure)
                return WheelWizardStatus.NoServer;

            switch (retroRewindStatus.Value)
            {
                case WheelWizardStatus.ConfigNotFinished:
                case WheelWizardStatus.NotInstalled:
                case WheelWizardStatus.OutOfDate:
                case WheelWizardStatus.NoServer:
                    return retroRewindStatus.Value;
            }

            var recompStatus = await installService.GetCurrentStatusAsync();
            if (recompStatus is not (WheelWizardStatus.Ready or WheelWizardStatus.NoServerButInstalled))
                return recompStatus;

            return
                retroRewindStatus.Value == WheelWizardStatus.NoServerButInstalled || recompStatus == WheelWizardStatus.NoServerButInstalled
                ? WheelWizardStatus.NoServerButInstalled
                : WheelWizardStatus.Ready;
        }
        catch (Exception)
        {
            return WheelWizardStatus.NoServer;
        }
    }

    /// <summary>
    /// A fresh install is the moment the user decides where WiiCompiled's Wii data comes from: their
    /// existing Dolphin NAND, a copy of it, or nothing. Dolphin advises against other programs using
    /// its NAND in place, so that trade-off is the user's call, not a silent default. An installed
    /// setup keeps whatever was chosen before; the Recomp Settings page is where that changes later.
    /// </summary>
    private async Task<OperationResult> AskForDolphinNandChoiceAsync()
    {
        if (installService.IsInstalled)
            return Ok();

        var sourceNand = await Task.Run(() => dolphinData.SourceNandFolderPath);
        if (sourceNand is null)
        {
            // There is no Dolphin data to offer, so the recomp simply starts with its own.
            dolphinData.SetSharingEnabled(false);
            dolphinData.SetCopyEnabled(false);
            return Ok();
        }

        var useDolphinData = await presentation.ConfirmUseDolphinDataAsync();
        if (!useDolphinData)
        {
            dolphinData.SetSharingEnabled(false);
            dolphinData.SetCopyEnabled(false);
            return Ok();
        }

        var copyNand = await presentation.ConfirmCopyDolphinDataAsync();
        if (!copyNand)
        {
            dolphinData.SetCopyEnabled(false);
            dolphinData.SetSharingEnabled(true);
            return Ok();
        }

        var copyResult = await presentation.RunAsync(RecompOperationKind.CopyNand, _ => Task.Run(dolphinData.CopyNandForRecomp));
        if (copyResult.IsFailure)
            return copyResult;

        // Flipped only after the copy durably exists, so a failed copy can never leave the settings
        // pointing at a NAND that is not there.
        dolphinData.SetCopyEnabled(true);
        dolphinData.SetSharingEnabled(false);
        return Ok();
    }

    private async Task<OperationResult> RunSetupAsync(RecompOperationKind kind)
    {
        try
        {
            return await presentation.RunAsync(
                kind,
                async operation =>
                {
                    var retroRewindResult = await EnsureRetroRewindCurrentAsync(operation.DistributionOperation);
                    if (retroRewindResult.IsFailure)
                        return operation.CancellationToken.IsCancellationRequested
                            ? CancellationWarning("WiiCompiled installation was cancelled.")
                            : retroRewindResult.Error;

                    var committed = retroRewindResult.Value;
                    if (!committed && operation.CancellationToken.IsCancellationRequested)
                        return CancellationWarning("WiiCompiled installation was cancelled.");

                    // Once RR succeeds, the matching recomp reconciliation must finish even if Cancel races completion.
                    operation.Report(new(CanCancel: !committed));
                    var result = await installService.InstallAsync(
                        operation.InstallProgress,
                        presentation.ConfirmOfflineInstallAsync,
                        committed ? CancellationToken.None : operation.CancellationToken
                    );
                    return result.IsFailure && !committed && operation.CancellationToken.IsCancellationRequested
                        ? CancellationWarning("WiiCompiled installation was cancelled.")
                        : result;
                }
            );
        }
        catch (OperationCanceledException)
        {
            return CancellationWarning("WiiCompiled installation was cancelled.");
        }
    }

    private static OperationError CancellationWarning(string message) => Fail(message, MessageTranslation.Warning_RecompOperationCancelled);

    private async Task<OperationResult<bool>> EnsureRetroRewindCurrentAsync(DistributionOperation operation)
    {
        operation.CancellationToken.ThrowIfCancellationRequested();
        var status = await customDistributions.RetroRewind.GetCurrentStatusAsync();
        if (status.IsFailure)
            return status.Error;

        var requiresCommit = status.Value is WheelWizardStatus.NotInstalled or WheelWizardStatus.OutOfDate;
        OperationResult result = status.Value switch
        {
            WheelWizardStatus.Ready or WheelWizardStatus.NoServerButInstalled => Ok(),
            WheelWizardStatus.NotInstalled => await customDistributions.RetroRewind.InstallAsync(operation),
            WheelWizardStatus.OutOfDate => await customDistributions.RetroRewind.UpdateAsync(operation),
            WheelWizardStatus.ConfigNotFinished => Fail(t("message_warning.not_find_game.extra")),
            WheelWizardStatus.NoServer => Fail("Retro Rewind could not be checked or installed because its update service is unavailable."),
            _ => Fail("Retro Rewind is not ready for WiiCompiled."),
        };

        if (result.IsFailure)
            return result.Error;
        if (!requiresCommit && operation.CancellationToken.IsCancellationRequested)
            return Fail("Retro Rewind update was cancelled.");
        return Ok(requiresCommit);
    }
}
