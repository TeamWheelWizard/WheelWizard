using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WheelWizard.ApplicationData;
using WheelWizard.ApplicationLifecycle.Logging;
using WheelWizard.DiscImages;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.GameFiles;
using WheelWizard.GameFiles.Views;
using WheelWizard.Models.Enums;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Desktop.Storage;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Shared.Processes;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.WiiManagement.GameLicense;
using SettingsButton = WheelWizard.Views.Components.ActionButton;

namespace WheelWizard.Settings.Views;

public partial class WhWzSettings : UserControl
{
    // #todo: move settings state and location-change workflows into a view model so this page mostly handles the controls.
    private readonly IApplicationLogFiles _logFiles;

    private bool _isMovingAppData;

    private IFilePickerService FilePicker { get; }

    private IDolphinPaths DolphinPaths { get; }

    private ISettingsManager SettingsService { get; }

    private IDolphinSettingManager DolphinSettingsService { get; }

    private IDolphinDiscoveryService DolphinDiscovery { get; }

    private IApplicationDataLocation ApplicationData { get; }

    private IDiscImageService DiscImages { get; }

    private IManagedDolphinService ManagedDolphin { get; }

    private IServiceProvider Services { get; }

    private IGameFilesService GameFiles => Services.GetRequiredService<IGameFilesService>();

    private IGameFilesPaths GameFilesPaths => Services.GetRequiredService<IGameFilesPaths>();

    public WhWzSettings(
        IApplicationLogFiles logFiles,
        IFilePickerService filePicker,
        IDolphinPaths dolphinPaths,
        ISettingsManager settingsService,
        IDolphinSettingManager dolphinSettingsService,
        IDolphinDiscoveryService dolphinDiscovery,
        IApplicationDataLocation applicationData,
        IDiscImageService discImages,
        IManagedDolphinService managedDolphin,
        IServiceProvider services
    )
    {
        DiscImages = discImages;
        ManagedDolphin = managedDolphin;
        Services = services;
        FilePicker = filePicker;
        DolphinPaths = dolphinPaths;
        SettingsService = settingsService;
        DolphinSettingsService = dolphinSettingsService;
        DolphinDiscovery = dolphinDiscovery;
        ApplicationData = applicationData;
        _logFiles = logFiles;
        InitializeComponent();
        ConfigureLocationFieldsForActiveFrontend();
        UpdateLocationRows();
        RefreshLocalizedCodeText();
        UpdateAppDataLocationUi();
    }

    private void ConfigureLocationFieldsForActiveFrontend()
    {
        var recompEnabled = SettingsService.IsRecompModeActive();
        DolphinExecutableField.IsVisible = !recompEnabled && !DolphinPaths.Layout.IsFlatpakSandboxed();
        GameLocationBorder.CornerRadius = recompEnabled ? new Avalonia.CornerRadius(12, 12, 5, 5) : new Avalonia.CornerRadius(5);
        DolphinUserFolderLabel.Text = recompEnabled
            ? $"{t("option.dolphin_user_path")} ({t("helper_text.optional")})"
            : t("option.dolphin_user_path");
        ToolTip.SetTip(LocationWarningIcon, recompEnabled ? t("helper_text.must_set_game_path") : t("helper_text.must_set_paths"));
    }

    private void RefreshLocalizedCodeText()
    {
        UpdateGameFileDetails();
        if (string.IsNullOrWhiteSpace(DolphinPaths.ExecutablePath))
            DolphinExecutableValueText.Text = GetDolphinExecutableHelperText();
    }

    private static string GetDolphinExecutableHelperText()
    {
        if (OperatingSystem.IsWindows())
            return t("helper_text.end_with_exe");
        if (OperatingSystem.IsLinux())
            return t("helper_text.select_dolphin_linux");
        if (OperatingSystem.IsMacOS())
            return t("helper_text.select_dolphin_macos");
        return t("helper_text.select_dolphin_linux");
    }

    private async void DolphinExeBrowse_OnClick(object sender, RoutedEventArgs e)
    {
        var executableFileType = new FilePickerFileType("Executable files")
        {
            Patterns = Environment.OSVersion.Platform switch
            {
                PlatformID.Win32NT => new[] { "*.exe" },
                PlatformID.Unix => new[] { "*", "*.sh" },
                PlatformID.MacOSX => new[] { "*", "*.app" },
                _ => new[] { "*" }, // Fallback
            },
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            const string selectFile = "Select executable file";
            const string enterCommand = "Enter launch command";
            var choice = await new OptionsWindow()
                .SetWindowTitle("Change Dolphin executable")
                .AddOption("FileImport", selectFile, () => { })
                .AddOption("Code", enterCommand, () => { })
                .AwaitAnswer();

            if (choice == enterCommand)
            {
                await EnterLinuxDolphinCommandAsync();
                return;
            }

            if (choice != selectFile)
                return;
        }

        if (DolphinPaths.Layout.IsFlatpakSandboxed())
        {
            // Having a picker does not make sense if Wheel Wizard is sandboxed.
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var dolphinAppPath = DolphinDiscovery.FindApplication();
            if (!string.IsNullOrEmpty(dolphinAppPath))
            {
                var result = await new YesNoWindow()
                    .SetMainText(t("question.dolphin_found.title"))
                    .SetExtraText($"{t("question.dolphin_found.extra")}\n{dolphinAppPath}")
                    .AwaitAnswer();

                if (result)
                {
                    await ApplyLocationSettingAsync(SettingsService.DOLPHIN_LOCATION, ShellQuoting.QuoteUnixArgument(dolphinAppPath));
                    return;
                }
            }
            else
            {
                await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Warning_DolphinNotFound);
            }

            // Fallback to manual selection
            var folders = await FilePicker.SelectFolderAsync("Select Dolphin.app");
            if (folders != null && folders.Count >= 1)
            {
                var resolvedFolder = await ResolveSelectedFolderPathAsync(folders[0]);
                if (string.IsNullOrWhiteSpace(resolvedFolder))
                    return;

                var executablePath = Path.Combine(resolvedFolder, "Contents", "MacOS", "Dolphin");
                await ApplyLocationSettingAsync(SettingsService.DOLPHIN_LOCATION, ShellQuoting.QuoteUnixArgument(executablePath));
            }

            return; // do not do normal selection for MacOS
        }

        var filePath = await FilePicker.OpenSingleFileAsync("Select Dolphin Emulator", [executableFileType]);
        if (!string.IsNullOrEmpty(filePath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (string.Equals(Path.GetFileName(filePath), "DolphinTool.exe", StringComparison.OrdinalIgnoreCase))
                {
                    await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Warning_DolphinToolSelected);
                    return;
                }

                // On Windows, the file path is directly used as the executable, not in some command
                await ApplyLocationSettingAsync(SettingsService.DOLPHIN_LOCATION, filePath);
            }
            else
            {
                await ApplyLocationSettingAsync(SettingsService.DOLPHIN_LOCATION, ShellQuoting.QuoteUnixArgument(filePath));
            }
        }
    }

    private async Task EnterLinuxDolphinCommandAsync()
    {
        var currentValue = DolphinPaths.ExecutablePath;
        var command = await new TextInputWindow()
            .SetMainText("Enter Dolphin launch command")
            .SetExtraText("Enter the terminal command Wheel Wizard should use to launch Dolphin.")
            .SetPlaceholderText("Example: flatpak run org.DolphinEmu.dolphin-emu")
            .SetInitialText(IsConfiguredExecutableFile(currentValue) ? string.Empty : currentValue)
            .SetButtonText(t("action.cancel"), t("action.save"))
            .SetValidation((_, value) => string.IsNullOrWhiteSpace(value) ? Fail("Enter a launch command.") : Ok())
            .ShowDialog();

        if (command != null)
            await ApplyLocationSettingAsync(SettingsService.DOLPHIN_LOCATION, command.Trim());
    }

    private async void GameLocationBrowse_OnClick(object sender, RoutedEventArgs e)
    {
        var fileType = new FilePickerFileType("Game files")
        {
            Patterns = DiscImages.SupportedExtensions.Select(extension => "*" + extension).ToArray(),
        };

        var filePath = await FilePicker.OpenSingleFileAsync("Select Mario Kart Wii Game File", [fileType]);
        if (string.IsNullOrEmpty(filePath))
            return;

        // The flow checks the file, asks before replacing an extracted game, and extracts it.
        var changed = await Services.GetRequiredService<IGameFilesFlow>().ChangeImageAsync(filePath);
        UpdateLocationRows();
        if (!changed)
            return;

        // A game from another region switches to that region's save.
        var layout = ViewUtils.GetLayout();
        layout.UpdateSidebarProfile();
        layout.UpdateFriendCount();
        await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Success_PathSettingsSaved);
    }

    private void UpdateGameFileDetails()
    {
        var status = GameFiles.GetStatus();
        var region = GameRegion.FromGameId(status.Manifest?.GameId);
        MarioKartHelperText.Text =
            status.Manifest is not null && region != MarioKartWiiEnums.Regions.None
                ? $"Mario Kart Wii · {GameRegion.GetDisplayName(region)} · {t("game_files.status_extracted")}"
                : t("helper_text.end_with_x") + " " + string.Join("/", DiscImages.SupportedExtensions);
        // The image the game was extracted from, which is no longer needed but good to know.
        ToolTip.SetTip(MarioKartHelperText, status.ImagePath);
    }

    private async void DolphinUserPathBrowse_OnClick(object sender, RoutedEventArgs e)
    {
        var folderPath = DolphinDiscovery.FindUserDirectory(
            SettingsService.Get<string>(SettingsService.DOLPHIN_LOCATION),
            SettingsService.Get<string>(SettingsService.USER_FOLDER_PATH)
        );

        if (!string.IsNullOrEmpty(folderPath))
        {
            // Ask the user if they want to use the automatically found folder
            var result = await new YesNoWindow()
                .SetMainText(t("question.dolphin_found.title"))
                .SetExtraText($"{t("question.dolphin_found.extra")}\n{folderPath}")
                .AwaitAnswer();

            if (result)
            {
                await ApplyLocationSettingAsync(SettingsService.USER_FOLDER_PATH, folderPath);
                return;
            }
        }
        else
        {
            await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Warning_DolphinNotFound);
        }

        var currentFolder = (string)SettingsService.USER_FOLDER_PATH.Get();
        var topLevel = TopLevel.GetTopLevel(this);
        // If a current folder exists and is valid, suggest it as the starting location
        if (!string.IsNullOrEmpty(currentFolder) && Directory.Exists(currentFolder))
        {
            var folder = await topLevel!.StorageProvider.TryGetFolderFromPathAsync(currentFolder);
            var folders = await FilePicker.SelectFolderAsync("Select Dolphin User Path", folder);

            if (folders != null && folders.Count >= 1)
            {
                var resolvedFolder = await ResolveSelectedFolderPathAsync(folders[0]);
                if (!string.IsNullOrWhiteSpace(resolvedFolder))
                    await ApplyLocationSettingAsync(SettingsService.USER_FOLDER_PATH, resolvedFolder);
            }
            return;
        }
        else
        {
            // Let the user manually select a folder
            var manualFolders = await FilePicker.SelectFolderAsync("Select Dolphin User Path");

            if (manualFolders != null && manualFolders.Count >= 1)
            {
                var resolvedFolder = await ResolveSelectedFolderPathAsync(manualFolders[0]);
                if (!string.IsNullOrWhiteSpace(resolvedFolder))
                    await ApplyLocationSettingAsync(SettingsService.USER_FOLDER_PATH, resolvedFolder);
            }
        }
    }

    private async Task<bool> ApplyLocationSettingAsync(Setting<string> setting, string path)
    {
        var normalizedPath = setting == SettingsService.USER_FOLDER_PATH ? Path.TrimEndingDirectorySeparator(path) : path;

        if (!SettingsEditing.Set(SettingsService, setting, normalizedPath))
        {
            UpdateLocationRows();
            return false;
        }

        // Picking a Dolphin or user folder by hand means the user now manages Dolphin themselves.
        if ((setting == SettingsService.DOLPHIN_LOCATION || setting == SettingsService.USER_FOLDER_PATH) && ManagedDolphin.IsActive)
            ManagedDolphin.Deactivate();

        UpdateLocationRows();

        await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Success_PathSettingsSaved);
        return true;
    }

    private void DolphinExecutableOpen_OnClick(object sender, RoutedEventArgs e) => OpenContainingFolder(DolphinPaths.ExecutablePath);

    private void GameLocationOpen_OnClick(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(GameFilesPaths.RootPath))
            FilePicker.OpenFolderInFileManager(GameFilesPaths.RootPath);
    }

    private void DolphinUserFolderOpen_OnClick(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(DolphinPaths.UserFolderPath))
            FilePicker.OpenFolderInFileManager(DolphinPaths.UserFolderPath);
    }

    private void OpenContainingFolder(string filePath)
    {
        try
        {
            var unquotedPath = filePath.Trim().Trim('\'', '"');
            var folderPath = Path.GetDirectoryName(Path.GetFullPath(unquotedPath));
            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                FilePicker.OpenFolderInFileManager(folderPath);
        }
        catch
        {
            // Commands such as Flatpak launch strings do not have a folder to open.
        }
    }

    private void AppDataLocationOpen_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(ApplicationData.DirectoryPath))
            Directory.CreateDirectory(ApplicationData.DirectoryPath);

        FilePicker.OpenFolderInFileManager(ApplicationData.DirectoryPath);
    }

    private void UpdateLocationRows()
    {
        SetLocationRowState(
            DolphinExecutableCompleteIcon,
            DolphinExecutableWarningIcon,
            DolphinExecutableChangeButton,
            SettingsService.DOLPHIN_LOCATION.IsValid() && !string.IsNullOrWhiteSpace(DolphinPaths.ExecutablePath)
        );
        SetLocationRowState(GameLocationCompleteIcon, GameLocationWarningIcon, GameLocationChangeButton, GameFiles.GetStatus().IsReady);
        SetLocationRowState(
            DolphinUserFolderCompleteIcon,
            DolphinUserFolderWarningIcon,
            DolphinUserFolderChangeButton,
            SettingsService.USER_FOLDER_PATH.IsValid()
        );

        LocationWarningIcon.IsVisible = !SettingsService.PathsSetupCorrectly();
        DolphinExecutableValueText.Text =
            ManagedDolphin.IsActive ? ManagedDolphinText()
            : string.IsNullOrWhiteSpace(DolphinPaths.ExecutablePath) ? GetDolphinExecutableHelperText()
            : DolphinPaths.ExecutablePath;
        UpdateManagedDolphinActions();
        DolphinExecutableOpenButton.IsVisible = !OperatingSystem.IsLinux() || IsConfiguredExecutableFile(DolphinPaths.ExecutablePath);
        DolphinExecutableOpenButton.IsEnabled =
            DolphinExecutableOpenButton.IsVisible && CanOpenContainingFolder(DolphinPaths.ExecutablePath);
        GameLocationOpenButton.IsEnabled = Directory.Exists(GameFilesPaths.RootPath);
        UpdateGameFileDetails();
        DolphinUserFolderOpenButton.IsEnabled = Directory.Exists(DolphinPaths.UserFolderPath);
    }

    private string ManagedDolphinText()
    {
        var version = ManagedDolphin.Kind == ManagedDolphinKind.Portable ? ManagedDolphin.InstalledVersion : null;
        return version is null ? t("onboarding.managed_dolphin_label") : $"{t("onboarding.managed_dolphin_label")} · {version}";
    }

    private void UpdateManagedDolphinActions()
    {
        var canManage = ManagedDolphin.Kind is ManagedDolphinKind.Portable or ManagedDolphinKind.Flatpak;
        ManageDolphinButton.IsVisible = canManage && !ManagedDolphin.IsActive && !SettingsService.IsRecompModeActive();
        RemoveManagedDolphinButton.IsVisible =
            ManagedDolphin.Kind == ManagedDolphinKind.Portable && ManagedDolphin.IsInstalled && ManagedDolphin.CanRemove;
        ManagedDolphinActions.IsVisible = ManageDolphinButton.IsVisible || RemoveManagedDolphinButton.IsVisible;
    }

    private async void ManageDolphin_OnClick(object? sender, RoutedEventArgs e)
    {
        var confirmed = await new YesNoWindow()
            .SetMainText(t("onboarding.managed_dolphin_switch"))
            .SetExtraText(t("onboarding.managed_dolphin_switch_question"))
            .AwaitAnswer();
        if (!confirmed)
            return;

        // Read the current Dolphin's data folders before the switch points the settings elsewhere.
        var sourceWii = SettingsService.USER_FOLDER_PATH.IsValid() ? DolphinPaths.WiiFolderPath : null;
        var sourceConfig = SettingsService.USER_FOLDER_PATH.IsValid() ? DolphinPaths.ConfigFolderPath : null;
        // The switch points the settings at the managed Dolphin before the import; a failure goes back to the user's own.
        var previousLocation = SettingsService.Get(SettingsService.DOLPHIN_LOCATION);
        var previousUserFolder = SettingsService.Get(SettingsService.USER_FOLDER_PATH);
        var wasManaged = ManagedDolphin.IsActive;

        var window = new ProgressWindow(t("onboarding.dolphin_installing_title"));
        OperationResult result;
        try
        {
            window.Show();
            var progress = new Progress<ManagedDolphinProgress>(value =>
            {
                window.SetExtraText(value.Message);
                window.UpdateProgress(value.Percent);
            });
            result = ManagedDolphin.IsInstalled ? ManagedDolphin.Activate() : await ManagedDolphin.InstallAsync(progress);
            if (result.IsSuccess && sourceWii is not null && sourceConfig is not null)
                result = await Task.Run(() => ManagedDolphin.ImportUserData(sourceWii, sourceConfig));
        }
        finally
        {
            window.Close();
        }

        if (result.IsFailure && !wasManaged)
        {
            ManagedDolphin.Deactivate();
            SettingsService.Set(SettingsService.DOLPHIN_LOCATION, previousLocation);
            SettingsService.Set(SettingsService.USER_FOLDER_PATH, previousUserFolder);
        }

        UpdateLocationRows();
        if (result.IsFailure)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("onboarding.dolphin_install_failed"))
                .SetInfoText(result.Error.Message)
                .ShowDialog();
            return;
        }

        RefreshLicensesAfterDolphinChange();
    }

    private async void RemoveManagedDolphin_OnClick(object? sender, RoutedEventArgs e)
    {
        var size = await Task.Run(ManagedDolphin.GetInstalledSize);
        var confirmed = await new YesNoWindow()
            .SetMainText(t("onboarding.managed_dolphin_remove"))
            .SetExtraText(t("onboarding.managed_dolphin_remove_question", new { size = $"{size / (1024d * 1024d):0} MB" }))
            .AwaitAnswer();
        if (!confirmed)
            return;

        var result = await Task.Run(ManagedDolphin.Remove);
        UpdateLocationRows();
        if (result.IsFailure)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("onboarding.managed_dolphin_remove"))
                .SetInfoText(result.Error.Message)
                .ShowDialog();
        }
    }

    private static void RefreshLicensesAfterDolphinChange()
    {
        var layout = ViewUtils.GetLayout();
        layout.UpdateSidebarProfile();
        layout.UpdateFriendCount();
    }

    /// <summary>Everything Wheel Wizard derives from its data folder has to follow it to the new place.</summary>
    private void FollowRelocatedDataFolder()
    {
        ManagedDolphin.SyncSettings();
        var result = Services.GetService<IRecompDolphinDataService>()?.ApplyPathsToRecompConfig();
        if (result is { IsFailure: true })
            Serilog.Log.Warning("Could not update WiiCompiled after moving the data folder: {Message}", result.Error.Message);
        UpdateLocationRows();
    }

    private static void SetLocationRowState(PathIcon completeIcon, PathIcon warningIcon, SettingsButton changeButton, bool isValid)
    {
        completeIcon.IsVisible = isValid;
        warningIcon.IsVisible = !isValid;
        changeButton.Tone = isValid ? ActionButtonTone.Secondary : ActionButtonTone.Brand;
    }

    private static bool CanOpenContainingFolder(string filePath)
    {
        try
        {
            var unquotedPath = filePath.Trim().Trim('\'', '"');
            var folderPath = Path.GetDirectoryName(Path.GetFullPath(unquotedPath));
            return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsConfiguredExecutableFile(string value) => File.Exists(value.Trim().Trim('\'', '"'));

    private void UpdateAppDataLocationUi()
    {
        var statusText =
            _isMovingAppData ? t("status.data_folder.moving")
            : ApplicationData.IsCustom ? t("status.data_folder.custom")
            : string.Empty;

        AppDataLocationStatus.Text = statusText;
        AppDataLocationStatus.IsVisible = !string.IsNullOrEmpty(statusText);
        AppDataLocationChangeButton.IsEnabled = !_isMovingAppData;
        AppDataLocationResetButton.IsEnabled = !_isMovingAppData && ApplicationData.IsCustom;
        AppDataLocationOpenButton.IsEnabled = !_isMovingAppData;
        ToolTip.SetTip(AppDataLocationResetButton, ApplicationData.DefaultDirectoryPath);
    }

    private void SetAppDataLocationBusyState(bool isBusy)
    {
        _isMovingAppData = isBusy;
        AppDataLocationChangeButton.IsEnabled = !isBusy;
        AppDataLocationResetButton.IsEnabled = !isBusy && ApplicationData.IsCustom;
        AppDataLocationOpenButton.IsEnabled = !isBusy;
        if (isBusy)
        {
            AppDataLocationStatus.Text = t("status.data_folder.moving");
            AppDataLocationStatus.IsVisible = true;
        }
    }

    private async Task<bool> ConfirmAndMoveAppDataAsync(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        var trimmedTarget = targetPath.Trim();

        var validationSuccessful = ApplicationData.TryValidateTarget(
            trimmedTarget,
            out var normalizedTarget,
            out _,
            out var validationError,
            out var requiresMove
        );

        if (!validationSuccessful)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("message_error.data_folder_move.title"))
                .SetInfoText(validationError)
                .ShowDialog();
            return false;
        }

        if (!requiresMove)
            return false;

        var extraText =
            t("question.move_data.extra", new { path = normalizedTarget })
            ?? $"Wheel Wizard will move its files to:\n{normalizedTarget}\nThis may take a while depending on the amount of data.";

        var confirmed = await new YesNoWindow()
            .SetMainText(t("question.move_data.title"))
            .SetExtraText(extraText)
            .SetButtonText(t("action.yes"), t("action.no"))
            .AwaitAnswer();

        if (!confirmed)
            return false;

        await MoveWheelWizardDataAsync(normalizedTarget);
        return true;
    }

    private async Task MoveWheelWizardDataAsync(string targetPath)
    {
        SetAppDataLocationBusyState(true);
        try
        {
            // Keep logs unchanged until the user has finished deciding whether to revert.
            using var loggingPause = _logFiles.Pause();

            var progressWindow = new ProgressWindow(t("status.data_folder.moving"))
                .SetExtraText(t("helper_text.wheel_wizard_data_folder"))
                .SetGoal(t("status.data_folder.moving"));
            progressWindow.Show();

            var progress = new Progress<double>(value =>
            {
                var percentage = (int)Math.Clamp(Math.Round(value * 100), 0, 100);
                progressWindow.UpdateProgress(percentage);
            });

            (bool success, string errorMessage, DirectoryMoveContentsResult details) moveResult;
            try
            {
                moveResult = await Task.Run(() =>
                {
                    var moveSuccessful = ApplicationData.TryMove(targetPath, out var error, out var moveDetails, progress);
                    return (moveSuccessful, error, moveDetails);
                });
            }
            catch (Exception ex)
            {
                progressWindow.Close();

                await new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Error)
                    .SetTitleText(t("message_error.data_folder_move.title"))
                    .SetInfoText(ex.Message)
                    .ShowDialog();
                return;
            }

            progressWindow.Close();

            var (success, errorMessage, details) = moveResult;

            if (success)
            {
                await HandleSuccessfulAppdataMoveAsync(details, errorMessage);
                FollowRelocatedDataFolder();
                // The extracted game moved with the data folder; WiiCompiled still names the old place until relinked.
                await Services.GetRequiredService<IGameFilesFlow>().LinkWiiCompiledAsync();
            }
            else
            {
                await HandleFailedAppdataMoveAsync(details, errorMessage);
            }
        }
        finally
        {
            SetAppDataLocationBusyState(false);
            UpdateAppDataLocationUi();
        }
    }

    private async Task HandleSuccessfulAppdataMoveAsync(DirectoryMoveContentsResult moveDetails, string warningMessage)
    {
        if (moveDetails.Outcome == DirectoryMoveOutcome.SourceDeletionFailed)
        {
            var prompt = new YesNoWindow()
                .SetMainText("Unable to delete old data folder")
                .SetExtraText(
                    "The previous Wheel Wizard data folder could not be removed."
                        + $"\n\nOld location:\n{moveDetails.SourcePath}\n\n"
                        + $"New location:\n{moveDetails.DestinationPath}\n\n"
                        + "Select Revert to undo the move or Continue to keep using the new folder and leave the old files."
                )
                .SetButtonText("Revert", "Continue");

            var revert = await prompt.AwaitAnswer();
            if (revert)
            {
                var (revertSucceeded, revertError) = await Task.Run(() =>
                {
                    var success = ApplicationData.TryRevertMove(moveDetails.SourcePath, moveDetails.DestinationPath, out var error);
                    return (success, error);
                });

                if (!revertSucceeded)
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText(t("message_error.data_folder_move.title"))
                        .SetInfoText(revertError)
                        .ShowDialog();
                }
                else
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Message)
                        .SetTitleText("Data folder move reverted")
                        .SetInfoText($"Wheel Wizard will continue using:\n{moveDetails.SourcePath}")
                        .ShowDialog();
                }

                return;
            }
        }

        var infoText =
            t("message_success.data_folder_moved.extra", new { path = ApplicationData.DirectoryPath })
            ?? $"Wheel Wizard data is now stored in:\n{ApplicationData.DirectoryPath}";

        if (!string.IsNullOrWhiteSpace(warningMessage))
            infoText += $"\n\n{warningMessage}";

        await new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Message)
            .SetTitleText(t("message_success.data_folder_moved.title"))
            .SetInfoText(infoText)
            .ShowDialog();
    }

    private async Task HandleFailedAppdataMoveAsync(DirectoryMoveContentsResult moveDetails, string errorMessage)
    {
        var infoText = string.IsNullOrWhiteSpace(errorMessage) ? "Failed to move the Wheel Wizard data folder." : errorMessage;

        await new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTitleText(t("message_error.data_folder_move.title"))
            .SetInfoText(infoText)
            .ShowDialog();

        if (moveDetails.Outcome is DirectoryMoveOutcome.CopyFailed or DirectoryMoveOutcome.VerificationFailed)
        {
            var prompt = new YesNoWindow()
                .SetMainText("Revert changes?")
                .SetExtraText(
                    $"Wheel Wizard left a partial copy in:\n{moveDetails.DestinationPath}\n\n"
                        + "Choose Revert to delete it now, or Continue to leave the files in place."
                )
                .SetButtonText("Revert", "Continue");

            var revert = await prompt.AwaitAnswer();
            if (revert)
            {
                var cleaned = ApplicationData.TryCleanupPartialMove(moveDetails.DestinationPath, out var cleanupError);
                if (!cleaned)
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText("Unable to remove partial files")
                        .SetInfoText(cleanupError)
                        .ShowDialog();
                }
                else
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Message)
                        .SetTitleText("Partial files removed")
                        .SetInfoText($"Removed folder:\n{moveDetails.DestinationPath}")
                        .ShowDialog();
                }
            }
        }
    }

    private async void AppDataLocationChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isMovingAppData)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        IStorageFolder? suggestedStart = null;

        var currentPath = ApplicationData.DirectoryPath;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            suggestedStart = await topLevel!.StorageProvider.TryGetFolderFromPathAsync(currentPath);

        var folders = await FilePicker.SelectFolderAsync("Select Wheel Wizard data folder", suggestedStart);
        if (folders == null || folders.Count == 0)
            return;

        var resolvedPath = await ResolveSelectedFolderPathAsync(folders[0]);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            await ConfirmAndMoveAppDataAsync(resolvedPath);
    }

    private async void AppDataLocationReset_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isMovingAppData || !ApplicationData.IsCustom)
            return;

        await ConfirmAndMoveAppDataAsync(ApplicationData.DefaultDirectoryPath);
    }

    private async Task<string?> ResolveSelectedFolderPathAsync(IStorageFolder? folder)
    {
        if (folder == null)
            return null;

        var resolved = StoragePaths.TryResolveLocalPath(folder);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        await ShowFolderSelectionErrorAsync();
        return null;
    }

    private Task ShowFolderSelectionErrorAsync()
    {
        return new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTitleText(t("message_error.data_folder_move.title"))
            .SetInfoText("Wheel Wizard couldn't resolve the selected folder. Please choose a different location.")
            .ShowDialog();
    }
}
