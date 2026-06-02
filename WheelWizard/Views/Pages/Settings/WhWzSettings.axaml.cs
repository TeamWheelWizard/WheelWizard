using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Serilog;
using WheelWizard.DolphinInstaller;
using WheelWizard.Helpers;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Pages.Settings;

public partial class WhWzSettings : UserControlBase
{
    private sealed record LanguageDropdownItem(string Key, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly bool _pageLoaded;
    private bool _editingScale;
    private bool _isMovingAppData;
    private bool _updatingLanguageDropdown;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    [Inject]
    private ISettingsLocalizationService LocalizationService { get; set; } = null!;

    [Inject]
    private IDolphinSettingManager DolphinSettingsService { get; set; } = null!;

    [Inject]
    private ILinuxDolphinInstaller LinuxDolphinInstallerService { get; set; } = null!;

    public WhWzSettings()
    {
        InitializeComponent();
        AutoFillPaths();
        TogglePathSettings(false);
        LoadSettings();
        UpdateAppDataLocationUi();
        _pageLoaded = true;

        WhWzLanguageDropdown.SelectionChanged += WhWzLanguageDropdown_OnSelectionChanged;
    }

    private void LoadSettings()
    {
        // -----------------
        // Wheel Wizard Language Dropdown
        // -----------------
        RefreshLanguageDropdown();
        RefreshLocalizedCodeText();

        // -----------------
        // Window Scale settings
        // -----------------
        // IMPORTANT: Make sure that the number and percentage is always the last word in the string,
        // If you don't want this, you should change the code below that parses the string back to an actual value

        foreach (var scale in SettingValues.WindowScales)
        {
            WindowScaleDropdown.Items.Add(ScaleToString(scale));
        }

        var selectedItemText = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
        if (!WindowScaleDropdown.Items.Contains(selectedItemText))
            WindowScaleDropdown.Items.Add(selectedItemText);
        WindowScaleDropdown.SelectedItem = selectedItemText;

        EnableAnimations.IsChecked = (bool)SettingsService.ENABLE_ANIMATIONS.Get();
    }

    private void RefreshLanguageDropdown()
    {
        var currentWhWzLanguage = (string)SettingsService.WW_LANGUAGE.Get();
        _updatingLanguageDropdown = true;
        try
        {
            WhWzLanguageDropdown.Items.Clear();
            foreach (var (key, displayNameFactory) in SettingValues.WhWzLanguages)
            {
                WhWzLanguageDropdown.Items.Add(new LanguageDropdownItem(key, displayNameFactory()));
            }

            WhWzLanguageDropdown.SelectedItem = WhWzLanguageDropdown
                .Items.OfType<LanguageDropdownItem>()
                .FirstOrDefault(item => item.Key == currentWhWzLanguage);
        }
        finally
        {
            _updatingLanguageDropdown = false;
        }
    }

    private void RefreshLocalizedCodeText()
    {
        MKGameFieldLabel.TipText = t("helper_text.end_with_x") + " .wbfs/.iso/.rvz";
        TranslationsPercentageText.Text = t("text.language_translated_by", t("value.language.z_translators"));
        TranslationsPercentageText.IsVisible = t("value.language.z_translators") != "-";
    }

    private static string ScaleToString(double scale)
    {
        var percentageString = (int)Math.Round(scale * 100) + "%";
        if (SettingValues.WindowScales.Contains(scale))
            return percentageString;

        return t("state.custom") + ": " + percentageString;
    }

    private void AutoFillPaths()
    {
        if (DolphinExeInput.Text != "")
            return;

        var folderPath = PathManager.TryFindUserFolderPath();
        if (!string.IsNullOrEmpty(folderPath))
            DolphinUserPathInput.Text = folderPath;
    }

    private void AssignWrappedDolphinExeInput(string inputText)
    {
        DolphinExeInput.Text = EnvHelper.SingleQuotePath(inputText);
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
            if (IsFlatpakDolphinInstalled() && DolphinExeInput.Text == "")
            {
                DolphinExeInput.Text = "flatpak run org.DolphinEmu.dolphin-emu";
                return;
            }

            if (!EnvHelper.IsFlatpakSandboxed() && !IsFlatpakDolphinInstalled())
            {
                var wantsAutomaticInstall = await new YesNoWindow()
                    .SetMainText(t("question.dolphin_flatpack.title"))
                    .SetExtraText(t("question.dolphin_flatpack.extra"))
                    .SetButtonText(t("action.install"), t("action.do_manually"))
                    .AwaitAnswer();
                if (wantsAutomaticInstall)
                {
                    var progressWindow = new ProgressWindow()
                        .SetGoal(t("progress.installing_dolphin"))
                        .SetExtraText(t("progress.this_may_take_a_while"));
                    TogglePathSettings(true);
                    progressWindow.Show();
                    var progress = new Progress<int>(progressWindow.UpdateProgress);
                    var installResult = await LinuxDolphinInstallerService.InstallFlatpakDolphin(progress);
                    progressWindow.Close();
                    if (installResult.IsFailure)
                    {
                        await new MessageBoxWindow()
                            .SetMessageType(MessageBoxWindow.MessageType.Error)
                            .SetTitleText("Failed to install Dolphin")
                            .SetInfoText(installResult.Error.Message)
                            .ShowDialog();
                        return;
                    }

                    DolphinExeInput.Text = "flatpak run org.DolphinEmu.dolphin-emu";
                    return;
                }
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var dolphinAppPath = PathManager.TryToFindApplicationPath();
            if (!string.IsNullOrEmpty(dolphinAppPath))
            {
                var result = await new YesNoWindow()
                    .SetMainText(t("question.dolphin_found.title"))
                    .SetExtraText($"{t("question.dolphin_found.extra")}\n{dolphinAppPath}")
                    .AwaitAnswer();

                if (result)
                {
                    AssignWrappedDolphinExeInput(dolphinAppPath);
                    return;
                }
            }
            else
            {
                await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Warning_DolphinNotFound);
            }

            // Fallback to manual selection
            var folders = await FilePickerHelper.SelectFolderAsync("Select Dolphin.app");
            if (folders != null && folders.Count >= 1)
            {
                var resolvedFolder = await ResolveSelectedFolderPathAsync(folders[0]);
                if (string.IsNullOrWhiteSpace(resolvedFolder))
                    return;

                var executablePath = Path.Combine(resolvedFolder, "Contents", "MacOS", "Dolphin");
                AssignWrappedDolphinExeInput(executablePath);
            }

            return; // do not do normal selection for MacOS
        }

        var filePath = await FilePickerHelper.OpenSingleFileAsync("Select Dolphin Emulator", [executableFileType]);
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
                DolphinExeInput.Text = filePath;
            }
            else
            {
                AssignWrappedDolphinExeInput(filePath);
            }
        }
    }

    private bool IsFlatpakDolphinInstalled()
    {
        return LinuxDolphinInstallerService.IsDolphinInstalledInFlatpak();
    }

    private async void GameLocationBrowse_OnClick(object sender, RoutedEventArgs e)
    {
        var fileType = new FilePickerFileType("Game files") { Patterns = ["*.iso", "*.wbfs", "*.rvz"] };

        var filePath = await FilePickerHelper.OpenSingleFileAsync("Select Mario Kart Wii Game File", [fileType]);
        if (!string.IsNullOrEmpty(filePath))
        {
            MarioKartInput.Text = filePath;
        }
    }

    private async void DolphinUserPathBrowse_OnClick(object sender, RoutedEventArgs e)
    {
        // Attempt to find Dolphin's default path if no valid folder is set
        var folderPath = PathManager.TryFindUserFolderPath();
        if (!string.IsNullOrEmpty(folderPath))
        {
            // Ask the user if they want to use the automatically found folder
            var result = await new YesNoWindow()
                .SetMainText(t("question.dolphin_found.title"))
                .SetExtraText($"{t("question.dolphin_found.extra")}\n{folderPath}")
                .AwaitAnswer();

            if (result)
            {
                DolphinUserPathInput.Text = folderPath;
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
            var folders = await FilePickerHelper.SelectFolderAsync("Select Dolphin User Path", folder);

            if (folders != null && folders.Count >= 1)
            {
                var resolvedFolder = await ResolveSelectedFolderPathAsync(folders[0]);
                if (!string.IsNullOrWhiteSpace(resolvedFolder))
                    DolphinUserPathInput.Text = resolvedFolder;
            }
            return;
        }
        else
        {
            // Let the user manually select a folder
            var manualFolders = await FilePickerHelper.SelectFolderAsync("Select Dolphin User Path");

            if (manualFolders != null && manualFolders.Count >= 1)
            {
                var resolvedFolder = await ResolveSelectedFolderPathAsync(manualFolders[0]);
                if (!string.IsNullOrWhiteSpace(resolvedFolder))
                    DolphinUserPathInput.Text = resolvedFolder;
            }
        }
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var oldPath1 = (string)SettingsService.DOLPHIN_LOCATION.Get();
        var oldPath2 = (string)SettingsService.GAME_LOCATION.Get();
        var oldPath3 = (string)SettingsService.USER_FOLDER_PATH.Get();

        var dolphinPath = DolphinExeInput.Text ?? string.Empty;
        var gamePath = MarioKartInput.Text ?? string.Empty;
        var userPathInput = DolphinUserPathInput.Text;
        var userPath = string.IsNullOrWhiteSpace(userPathInput) ? string.Empty : userPathInput.TrimEnd(Path.DirectorySeparatorChar);

        var path1 = SettingsService.DOLPHIN_LOCATION.Set(dolphinPath);
        var path2 = SettingsService.GAME_LOCATION.Set(gamePath);
        var path3 = SettingsService.USER_FOLDER_PATH.Set(userPath);
        // These 3 lines is only saving the settings
        TogglePathSettings(false);
        if (!(SettingsService.PathsSetupCorrectly() && path1 && path2 && path3))
            await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Warning_InvalidPathSettings);
        else
        {
            await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Success_PathSettingsSaved);

            // This is not really the best approach, but it works for now
            if (oldPath1 + oldPath2 + oldPath3 != DolphinExeInput.Text + MarioKartInput.Text + DolphinUserPathInput.Text)
                DolphinSettingsService.ReloadSettings();
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => TogglePathSettings(false);

    private void EditButton_OnClick(object sender, RoutedEventArgs e) => TogglePathSettings(true);

    private void WhWzFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(PathManager.WheelWizardAppdataPath))
            Directory.CreateDirectory(PathManager.WheelWizardAppdataPath);

        FilePickerHelper.OpenFolderInFileManager(PathManager.WheelWizardAppdataPath);
    }

    private void GameFileFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(PathManager.RiivolutionWhWzFolderPath))
            return; //Button should be disabled
        FilePickerHelper.OpenFolderInFileManager(PathManager.RiivolutionWhWzFolderPath);
    }

    private void TogglePathSettings(bool enable)
    {
        if (enable)
        {
            LocationBorder.BorderBrush = new SolidColorBrush(ViewUtils.Colors.Neutral900);
        }
        else if (!SettingsService.PathsSetupCorrectly())
        {
            LocationBorder.BorderBrush = new SolidColorBrush(ViewUtils.Colors.Warning400);
            LocationEditButton.Variant = Button.ButtonsVariantType.Warning;
            LocationWarningIcon.IsVisible = true;
            LocationBorderBlur.Background = new SolidColorBrush(ViewUtils.Colors.Warning600);
        }
        else
        {
            LocationBorder.BorderBrush = new SolidColorBrush(ViewUtils.Colors.Primary400);
            LocationEditButton.Variant = Button.ButtonsVariantType.Primary;
            LocationWarningIcon.IsVisible = false;
            LocationBorderBlur.Background = new SolidColorBrush(ViewUtils.Colors.Primary600);
        }

        LocationBorderBlur.IsVisible = !enable;
        LocationInputFields.IsEnabled = enable;
        LocationEditButton.IsVisible = !enable;
        LocationSaveButton.IsVisible = enable;
        LocationCancelButton.IsVisible = enable;

        DolphinExeInput.Text = PathManager.DolphinFilePath;
        MarioKartInput.Text = PathManager.GameFilePath;
        DolphinUserPathInput.Text = PathManager.UserFolderPath;
        OpenGameFolderButton.IsEnabled = Directory.Exists(PathManager.RiivolutionWhWzFolderPath);
        UpdateAppDataLocationUi();
    }

    private void UpdateAppDataLocationUi()
    {
        var currentPath = PathManager.WheelWizardAppdataPath;
        AppDataLocationInput.Text = currentPath;
        AppDataLocationInput.CaretIndex = currentPath.Length;
        ToolTip.SetTip(AppDataLocationInput, currentPath);

        var statusText =
            _isMovingAppData ? t("status.data_folder.moving")
            : PathManager.IsUsingCustomWheelWizardAppdataPath ? t("status.data_folder.custom")
            : t("status.data_folder.default");

        AppDataLocationStatus.Text = statusText;
        AppDataLocationBrowseButton.IsEnabled = !_isMovingAppData;
        AppDataLocationResetButton.IsEnabled = !_isMovingAppData && PathManager.IsUsingCustomWheelWizardAppdataPath;
        ToolTip.SetTip(AppDataLocationResetButton, PathManager.DefaultWheelWizardAppdataFolderPath);
    }

    private void SetAppDataLocationBusyState(bool isBusy)
    {
        _isMovingAppData = isBusy;
        AppDataLocationBrowseButton.IsEnabled = !isBusy;
        AppDataLocationResetButton.IsEnabled = !isBusy && PathManager.IsUsingCustomWheelWizardAppdataPath;
        if (isBusy)
            AppDataLocationStatus.Text = t("status.data_folder.moving");
    }

    private async Task<bool> ConfirmAndMoveAppDataAsync(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        var trimmedTarget = targetPath.Trim();

        var validationSuccessful = PathManager.TryValidateWheelWizardAppdataTarget(
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
            t("question.move_data.extra", normalizedTarget)
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
        Log.CloseAndFlush();

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
                var moveSuccessful = PathManager.TrySetWheelWizardAppdataPath(targetPath, out var error, out var moveDetails, progress);
                return (moveSuccessful, error, moveDetails);
            });
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            WheelWizard.Logging.RecreateStaticLogger();
            SetAppDataLocationBusyState(false);
            UpdateAppDataLocationUi();

            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("message_error.data_folder_move.title"))
                .SetInfoText(ex.Message)
                .ShowDialog();
            return;
        }

        progressWindow.Close();

        WheelWizard.Logging.RecreateStaticLogger();

        SetAppDataLocationBusyState(false);
        UpdateAppDataLocationUi();

        var (success, errorMessage, details) = moveResult;

        if (success)
        {
            await HandleSuccessfulAppdataMoveAsync(details, errorMessage);
        }
        else
        {
            await HandleFailedAppdataMoveAsync(details, errorMessage);
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
                var revertSucceeded = PathManager.TryRevertWheelWizardAppdataMove(
                    moveDetails.SourcePath,
                    moveDetails.DestinationPath,
                    out var revertError
                );
                WheelWizard.Logging.RecreateStaticLogger();
                UpdateAppDataLocationUi();

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
            t("message_success.data_folder_moved.extra", PathManager.WheelWizardAppdataPath)
            ?? $"Wheel Wizard data is now stored in:\n{PathManager.WheelWizardAppdataPath}";

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
                var cleaned = PathManager.TryCleanupPartialWheelWizardAppdataMove(moveDetails.DestinationPath, out var cleanupError);
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

    private async void AppDataLocationBrowse_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isMovingAppData)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        IStorageFolder? suggestedStart = null;

        var currentPath = PathManager.WheelWizardAppdataPath;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            suggestedStart = await topLevel!.StorageProvider.TryGetFolderFromPathAsync(currentPath);

        var folders = await FilePickerHelper.SelectFolderAsync("Select Wheel Wizard data folder", suggestedStart);
        if (folders == null || folders.Count == 0)
            return;

        var selected = folders[0];
        var resolvedPath = await ResolveSelectedFolderPathAsync(selected);
        if (string.IsNullOrWhiteSpace(resolvedPath))
            return;

        await ConfirmAndMoveAppDataAsync(resolvedPath);
    }

    private async void AppDataLocationReset_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isMovingAppData || !PathManager.IsUsingCustomWheelWizardAppdataPath)
            return;

        await ConfirmAndMoveAppDataAsync(PathManager.DefaultWheelWizardAppdataFolderPath);
    }

    private async void WindowScaleDropdown_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageLoaded || _editingScale)
            return;

        _editingScale = true;
        var selectedScale = WindowScaleDropdown.SelectedItem?.ToString() ?? "1";
        var scale = double.Parse(selectedScale.Split(" ").Last().Replace("%", "")) / 100;
        scale = ViewUtils.GetUsableWindowScale(scale, new Avalonia.Size(Layout.WindowWidth, Layout.WindowHeight), ViewUtils.GetLayout());
        var selectedItemText = ScaleToString(scale);
        if (!WindowScaleDropdown.Items.Contains(selectedItemText))
            WindowScaleDropdown.Items.Add(selectedItemText);
        WindowScaleDropdown.SelectedItem = selectedItemText;

        if (!SettingsService.WINDOW_SCALE.Set(scale))
        {
            WindowScaleDropdown.SelectedItem = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
            _editingScale = false;
            return;
        }
        var seconds = 10;

        string ExtraScaleText() => t("question.apply_scale.extra", tTime(seconds));

        var yesNoWindow = new YesNoWindow()
            .SetButtonText(t("action.apply"), t("action.revert"))
            .SetMainText(t("question.apply_scale.title"))
            .SetExtraText(ExtraScaleText());
        // we want to now set up a timer every second to update the text, and at the last second close the window
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        timer.Tick += (_, args) =>
        {
            seconds--;
            yesNoWindow.SetExtraText(ExtraScaleText());
            if (seconds != 0)
                return;
            yesNoWindow.Close();
            timer.Stop();
        };
        timer.Start();

        var yesNoAnswer = await yesNoWindow.AwaitAnswer();
        if (yesNoAnswer)
            SettingsService.SAVED_WINDOW_SCALE.Set(SettingsService.WINDOW_SCALE.Get());
        else
        {
            SettingsService.WINDOW_SCALE.Set(SettingsService.SAVED_WINDOW_SCALE.Get());
            WindowScaleDropdown.SelectedItem = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
        }

        _editingScale = false;
    }

    private async Task<string?> ResolveSelectedFolderPathAsync(IStorageFolder? folder)
    {
        if (folder == null)
            return null;

        var resolved = FilePickerHelper.TryResolveLocalPath(folder);
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

    private async void WhWzLanguageDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingLanguageDropdown)
            return;

        if (WhWzLanguageDropdown.SelectedItem == null)
            return;

        if (WhWzLanguageDropdown.SelectedItem is not LanguageDropdownItem selectedLanguage)
            return;

        var currentLanguage = (string)SettingsService.WW_LANGUAGE.Get();
        if (selectedLanguage.Key == currentLanguage)
            return;

        var titleCurrent = t($"{currentLanguage}.question.apply_language_settings.title");
        var titleTarget = t($"{selectedLanguage.Key}.question.apply_language_settings.title");

        var extraCurrent = t($"{currentLanguage}.question.apply_language_settings.extra");
        var extraTarget = t($"{selectedLanguage.Key}.question.apply_language_settings.extra");

        // popup now shows its selection in both languages
        var yesNoWindow = await new YesNoWindow()
            .SetMainText($"{titleCurrent}\n\n{titleTarget}")
            .SetExtraText($"{extraCurrent}\n\n{extraTarget}")
            .SetButtonText(t("action.apply"), t("action.cancel"))
            .AwaitAnswer();

        if (!yesNoWindow)
        {
            var currentWhWzLanguage = (string)SettingsService.WW_LANGUAGE.Get();
            WhWzLanguageDropdown.SelectedItem = WhWzLanguageDropdown
                .Items.OfType<LanguageDropdownItem>()
                .FirstOrDefault(item => item.Key == currentWhWzLanguage);
            return; // We only want to change the setting if we really apply this change
        }

        if (SettingsService.WW_LANGUAGE.Set(selectedLanguage.Key))
        {
            LocalizationService.ApplyCurrentLanguage();
            RefreshLanguageDropdown();
            RefreshLocalizedCodeText();
            ViewUtils.RefreshWindow();
        }
    }

    private void EnableAnimations_OnClick(object sender, RoutedEventArgs e) =>
        SettingsService.ENABLE_ANIMATIONS.Set(EnableAnimations.IsChecked == true);
}
