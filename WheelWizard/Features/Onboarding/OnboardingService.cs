using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomDistributions;
using WheelWizard.DiscImages;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.Models.Enums;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Domain;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Onboarding;

/// <summary>A Dolphin found on this machine, ready to be used as-is.</summary>
public sealed record DetectedDolphin(string Location, string DisplayPath, string? Version);

/// <summary>Another Dolphin's data that a managed Dolphin can import.</summary>
public sealed record DetectedDolphinData(string UserFolderPath, string WiiFolderPath, string ConfigFolderPath);

public sealed record GameFileCheck(bool IsValid, string Message);

public interface IOnboardingService
{
    /// <summary>Nothing was ever set up and the onboarding was never finished.</summary>
    bool IsRequired { get; }

    /// <summary>
    /// Marks a setup that works, or that was set up before, as onboarded, so a later breakage never brings the
    /// onboarding back; Home's repair banner covers that instead.
    /// </summary>
    void MarkCompletedIfAlreadySetUp();

    bool IsWiiCompiledAvailable { get; }
    ManagedDolphinKind ManagedDolphinKind { get; }

    /// <summary>A usable Dolphin that needs no choice at all (the Flatpak's bundled extension).</summary>
    bool HasBundledDolphin { get; }

    /// <summary>The Wheel Wizard Flatpak was installed without its Dolphin extension.</summary>
    bool IsMissingBundledDolphin { get; }

    DetectedDolphin? DetectDolphin();
    DetectedDolphinData? DetectDolphinData();

    /// <summary>Uses a detected or picked Dolphin as the user's own (not managed).</summary>
    OperationResult UseOwnDolphin(string location);

    /// <summary>The Flatpak's own Dolphin: existing Dolphin data when there is some, otherwise a managed user folder.</summary>
    OperationResult UseBundledDolphin();

    GameFileCheck CheckGameFile(string path);
    OperationResult SetGameFile(string path);

    /// <summary>Whether the WiiCompiled path has Dolphin data it can offer to share or copy.</summary>
    bool HasDolphinNandToOffer { get; }

    Task<OperationResult> ApplyNandChoiceAsync(RecompNandChoice choice);

    /// <summary>Finishes the Dolphin path; Retro Rewind may still be downloading.</summary>
    OperationResult FinishDolphin();

    /// <summary>Installs WiiCompiled once Retro Rewind is in place, then finishes the onboarding.</summary>
    Task<OperationResult> FinishWiiCompiledAsync(IProgress<RecompInstallProgress> progress, CancellationToken cancellationToken);
}

public sealed class OnboardingService(
    ISettingsManager settings,
    IFileSystem fileSystem,
    IRuntimeEnvironment environment,
    IDiscImageService discImages,
    IDolphinDiscoveryService discovery,
    IDolphinPathResolver dolphinPaths,
    IUnixCommandService commands,
    ILinuxDolphinInstaller linuxDolphin,
    IManagedDolphinService managedDolphin,
    IMiiRepositoryService miiRepository,
    IGameLicenseSingletonService licenses,
    ICustomDistributionPaths distributionPaths,
    IServiceProvider services,
    ILogger<OnboardingService> logger
) : IOnboardingService
{
    private IRecompInstallService? RecompInstall => services.GetService<IRecompInstallService>();
    private IRecompDolphinDataService? RecompData => services.GetService<IRecompDolphinDataService>();

    public bool IsRequired => !settings.Get(settings.ONBOARDING_COMPLETED) && !WasSetUpBefore() && !IsSetupUsable();

    public void MarkCompletedIfAlreadySetUp()
    {
        if (!settings.Get(settings.ONBOARDING_COMPLETED) && (WasSetUpBefore() || IsSetupUsable()))
            settings.Set(settings.ONBOARDING_COMPLETED, true);
    }

    /// <summary>
    /// Someone picked paths before, possibly before the onboarding existed. A path that does not resolve today
    /// (an unplugged drive, a moved Dolphin) needs repairing, not a first-run flow that would replace it.
    /// </summary>
    private bool WasSetUpBefore() => IsSet(settings.DOLPHIN_LOCATION) || IsSet(settings.USER_FOLDER_PATH) || IsSet(settings.GAME_LOCATION);

    // The Flatpak defaults the Dolphin location to its bundled wrapper, which nobody picked.
    private bool IsSet(Setting<string> setting) =>
        settings.Get(setting) is { } value && !string.IsNullOrWhiteSpace(value) && value != setting.DefaultValue;

    private bool IsSetupUsable()
    {
        if (settings.DolphinPathsSetupCorrectly())
            return true;
        return RecompInstall?.IsInstalled == true && settings.GAME_LOCATION.IsValid();
    }

    public bool IsWiiCompiledAvailable =>
        RecompPlatform.IsSupported && RecompInstall is not null && !environment.IsMissingRecompFlatpakExtension(fileSystem);

    public ManagedDolphinKind ManagedDolphinKind => managedDolphin.Kind;
    public bool HasBundledDolphin => managedDolphin.Kind == ManagedDolphinKind.Bundled && !IsMissingBundledDolphin;
    public bool IsMissingBundledDolphin => environment.IsMissingDolphinFlatpakExtension(fileSystem);

    public DetectedDolphin? DetectDolphin()
    {
        try
        {
            if (environment.IsMacOS)
            {
                var app = discovery.FindApplication();
                return app is null ? null : Detected(ShellQuoting.QuoteUnixArgument(app), app);
            }

            if (environment.IsLinux && !environment.IsFlatpakSandboxed(fileSystem))
            {
                if (linuxDolphin.IsDolphinInstalledInFlatpak())
                    return Detected(ManagedDolphinService.FlatpakCommand, ManagedDolphinService.FlatpakCommand);
                if (linuxDolphin.IsDolphinInstalledNative())
                    return Detected("dolphin-emu", "dolphin-emu");
            }

            // Windows Dolphin builds are plain archives without an install location to look in.
            return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Looking for an installed Dolphin failed");
            return null;
        }
    }

    private DetectedDolphin Detected(string location, string displayPath)
    {
        var layout = dolphinPaths.Resolve(location, string.Empty);
        string? version = null;
        try
        {
            var arguments = environment.IsLinux && !layout.IsFlatpakDolphinFilePath(location) ? "QT_QPA_PLATFORM=wayland;xcb " : "";
            var result = services
                .GetRequiredService<IUnixProcessService>()
                .Run("/usr/bin/env", ["sh", "-c", "--", $"{arguments}{location} --version"], out var stdOut, out var stdErr);
            if (result.IsSuccess && result.Value == 0)
                DolphinVersion.GetStatus(string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut, out version);
        }
        catch
        {
            // The version is only shown, never required.
        }

        return new(location, displayPath, version);
    }

    public DetectedDolphinData? DetectDolphinData()
    {
        try
        {
            var location = settings.Get(settings.DOLPHIN_LOCATION);
            var userFolder = discovery.FindUserDirectory(location, settings.Get(settings.USER_FOLDER_PATH));
            if (
                string.IsNullOrWhiteSpace(userFolder)
                || fileSystem.Path.PathsEqual(userFolder, managedDolphin.UserFolderPath)
                || settings.DOLPHIN_MANAGED.Get() && fileSystem.Path.PathsEqual(userFolder, settings.Get(settings.USER_FOLDER_PATH))
            )
                return null;

            var wii = fileSystem.Path.Combine(userFolder, "Wii");
            if (!fileSystem.Directory.Exists(wii))
                return null;
            return new(userFolder, wii, dolphinPaths.Resolve(location, userFolder).ConfigFolderPath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Looking for existing Dolphin data failed");
            return null;
        }
    }

    public OperationResult UseOwnDolphin(string location)
    {
        if (!settings.Set(settings.DOLPHIN_LOCATION, location))
            return Fail(t("onboarding.dolphin_invalid"));

        var userFolder = discovery.FindUserDirectory(location, settings.Get(settings.USER_FOLDER_PATH));
        if (string.IsNullOrWhiteSpace(userFolder) || !settings.Set(settings.USER_FOLDER_PATH, userFolder))
        {
            // A Dolphin that never ran has no user folder yet; give it the one Wheel Wizard would manage.
            fileSystem.Directory.CreateDirectory(managedDolphin.UserFolderPath);
            if (!settings.Set(settings.USER_FOLDER_PATH, managedDolphin.UserFolderPath))
                return Fail(t("onboarding.dolphin_user_folder_invalid"));
        }

        settings.Set(settings.DOLPHIN_MANAGED, false);
        return Ok();
    }

    public OperationResult UseBundledDolphin()
    {
        var data = DetectDolphinData();
        if (data is null)
            return managedDolphin.Activate();

        if (!settings.Set(settings.USER_FOLDER_PATH, data.UserFolderPath))
            return Fail(t("onboarding.dolphin_user_folder_invalid"));
        settings.Set(settings.DOLPHIN_MANAGED, false);
        return Ok();
    }

    public GameFileCheck CheckGameFile(string path)
    {
        var info = discImages.ReadInfo(path);
        if (info.IsFailure)
            return new(false, t("message_error.invalid_game_file.unreadable"));

        var region = GameRegion.FromGameId(info.Value.GameId);
        if (region == MarioKartWiiEnums.Regions.None)
            return new(
                false,
                t("message_error.invalid_game_file.wrong_game", new { name = info.Value.InternalName, id = info.Value.GameId })
            );

        return new(true, $"Mario Kart Wii · {GameRegion.GetDisplayName(region)} · {info.Value.Format.ToString().ToUpperInvariant()}");
    }

    public OperationResult SetGameFile(string path) =>
        CheckGameFile(path) is { IsValid: false } check ? Fail(check.Message)
        : settings.Set(settings.GAME_LOCATION, path) ? Ok()
        : Fail(t("message_error.invalid_game_file.unreadable"));

    public bool HasDolphinNandToOffer => RecompInstall?.IsInstalled == false && RecompData?.SourceNandFolderPath is not null;

    public Task<OperationResult> ApplyNandChoiceAsync(RecompNandChoice choice) =>
        RecompData is { } data ? Task.Run(() => data.ApplyNandChoice(choice)) : Task.FromResult(Ok());

    public OperationResult FinishDolphin()
    {
        settings.Set(settings.ENABLE_RECOMP, false);
        CreateMiiDatabase();
        return Complete();
    }

    public async Task<OperationResult> FinishWiiCompiledAsync(
        IProgress<RecompInstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        if (RecompInstall is not { } install || RecompData is not { } data)
            return Fail("WiiCompiled is not available on this system.");

        settings.Set(settings.ENABLE_RECOMP, true);
        if (!fileSystem.Directory.Exists(distributionPaths.RetroRewindFolderPath))
            return Fail(t("onboarding.rr_missing"));

        var result = await install.InstallAsync(progress, confirmOfflineInstall: null, cancellationToken);
        if (result.IsFailure)
            return result;

        var paths = await Task.Run(data.ApplyPathsToRecompConfig, cancellationToken);
        if (paths.IsFailure)
            return paths;

        CreateMiiDatabase();
        return Complete();
    }

    /// <summary>The game only creates its Mii database on first boot; creating an empty one lets the Mii editor work right away.</summary>
    private void CreateMiiDatabase()
    {
        try
        {
            if (miiRepository.Exists())
                return;
            var result = miiRepository.ForceCreateDatabase();
            if (result.IsFailure)
                logger.LogWarning("Could not create an empty Mii database: {Message}", result.Error.Message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not create an empty Mii database");
        }
    }

    private OperationResult Complete()
    {
        settings.Set(settings.ONBOARDING_COMPLETED, true);
        licenses.LoadLicense();
        return Ok();
    }
}
