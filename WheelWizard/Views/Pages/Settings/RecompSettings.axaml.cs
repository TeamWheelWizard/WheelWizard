using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Recomp;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Pages.Settings;

public partial class RecompSettings : UserControlBase
{
    private bool _loading;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    [Inject]
    private IRecompSettingManager RecompSettingsFile { get; set; } = null!;

    [Inject]
    private IRecompDolphinDataService? DolphinData { get; set; }

    [Inject]
    private IRecompEnvironment? RecompEnvironment { get; set; }

    [Inject]
    private IRecompInstallService? RecompInstallService { get; set; }

    public RecompSettings()
    {
        InitializeComponent();

        // Config.toml is also written by the in-game settings bar, so opening the page rereads the
        // file rather than trusting whatever was loaded at startup.
        RecompSettingsFile.ReloadSettings();
        LoadSettings();

        // Attached after loading, so populating a control never writes it straight back.
        UseDolphinData.IsCheckedChanged += UseDolphinData_OnChanged;
        ResolutionDropdown.SelectionChanged += Resolution_OnChanged;
        GraphicsApiDropdown.SelectionChanged += GraphicsApi_OnChanged;
        ShowFps.IsCheckedChanged += ShowFps_OnChanged;
        PreventStutters.IsCheckedChanged += PreventStutters_OnChanged;
    }

    private bool IsInstalled => RecompInstallService is { IsInstalled: true };

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var installed = IsInstalled;
            NotInstalledText.IsVisible = !installed;
            VideoSectionLabel.IsVisible = installed;
            VideoBorder.IsVisible = installed;
            InstallationSectionLabel.IsVisible = installed;
            InstallationBorder.IsVisible = installed;

            var installFolder = RecompEnvironment?.InstallFolderPath ?? PathManager.RecompInstallFolderPath;
            InstallLocation.Text = installFolder;
            OpenInstallFolder.IsEnabled = Directory.Exists(installFolder);

            LoadVideoSettings();

            // The sharing state the next install/launch will actually use, not just the preference flag.
            var nandFolder = DolphinData?.NandFolderPath;
            UseDolphinData.IsChecked = DolphinData is { IsEnabled: true } && nandFolder is not null;
            DolphinDataStatus.Text =
                nandFolder is null ? t("status.recomp_dolphin_data_not_linked")
                : DolphinData is { CopyEnabled: true } ? t("status.recomp_dolphin_data_copied", nandFolder)
                : t("status.recomp_dolphin_data_linked", nandFolder);
        }
        finally
        {
            _loading = false;
        }
    }

    #region WiiCompiled video settings

    private void LoadVideoSettings()
    {
        ResolutionDropdown.Items.Clear();
        foreach (var multiplier in RecompVideoConfig.ResolutionMultipliers)
        {
            ResolutionDropdown.Items.Add(RecompVideoConfig.DescribeResolution(multiplier));
        }

        ResolutionDropdown.SelectedIndex = FindClosestResolutionIndex(
            SettingsService.Get<double>(SettingsService.RECOMP_RESOLUTION_MULTIPLIER)
        );

        GraphicsApiDropdown.Items.Clear();
        foreach (var api in RecompVideoConfig.OfferedGraphicsApis)
        {
            GraphicsApiDropdown.Items.Add(RecompVideoConfig.DescribeGraphicsApi(api));
        }

        // A value outside the offered list (such as the backend default "auto") simply shows no
        // selection; it is replaced the moment the user picks a real one.
        var currentApi = SettingsService.Get<string>(SettingsService.RECOMP_GRAPHICS_API);
        GraphicsApiDropdown.SelectedIndex = RecompVideoConfig.OfferedGraphicsApis.ToList().IndexOf(currentApi);

        ShowFps.IsChecked = SettingsService.Get<bool>(SettingsService.RECOMP_SHOW_FPS);
        PreventStutters.IsChecked = SettingsService.Get<bool>(SettingsService.RECOMP_PREVENT_STUTTERS);
    }

    /// <summary>
    /// Maps whatever multiplier the recomp currently holds onto the nearest offered option, so a value
    /// set outside Wheel Wizard is shown as the closest thing rather than as an empty dropdown.
    /// </summary>
    private static int FindClosestResolutionIndex(double multiplier)
    {
        var multipliers = RecompVideoConfig.ResolutionMultipliers;
        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < multipliers.Count; index++)
        {
            var distance = Math.Abs(multipliers[index] - multiplier);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = index;
        }

        return bestIndex;
    }

    private void Resolution_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = ResolutionDropdown.SelectedIndex;
        if (_loading || index < 0 || index >= RecompVideoConfig.ResolutionMultipliers.Count)
            return;

        SettingsService.Set(SettingsService.RECOMP_RESOLUTION_MULTIPLIER, RecompVideoConfig.ResolutionMultipliers[index]);
    }

    private void GraphicsApi_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = GraphicsApiDropdown.SelectedIndex;
        if (_loading || index < 0 || index >= RecompVideoConfig.OfferedGraphicsApis.Count)
            return;

        SettingsService.Set(SettingsService.RECOMP_GRAPHICS_API, RecompVideoConfig.OfferedGraphicsApis[index]);
    }

    private void ShowFps_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        SettingsService.Set(SettingsService.RECOMP_SHOW_FPS, ShowFps.IsChecked == true);
    }

    private void PreventStutters_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        SettingsService.Set(SettingsService.RECOMP_PREVENT_STUTTERS, PreventStutters.IsChecked == true);
    }

    #endregion

    private async void UseDolphinData_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading || DolphinData is null)
            return;

        if (UseDolphinData.IsChecked != true)
        {
            DolphinData.SetEnabled(false);
            await ApplyNandSettingAsync();
            LoadSettings();
            return;
        }

        var userFolder = DolphinData.LinkedUserFolderPath ?? await Task.Run(DolphinData.FindCandidateUserFolder);
        if (userFolder is null)
        {
            DolphinData.SetEnabled(false);
            await ApplyNandSettingAsync();
            LoadSettings();
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText(t("status.recomp_dolphin_data_not_found"))
                .SetInfoText(t("helper_text.recomp_dolphin_data_not_found"))
                .ShowDialog();
            return;
        }

        var result = DolphinData.Link(userFolder);
        if (result.IsSuccess && DolphinData.CopyEnabled && DolphinData.NandFolderPath is null)
        {
            // The copy this install once made is gone (for example after an uninstall), so turning
            // sharing back on falls back to the in-place link the toggle describes.
            DolphinData.SetCopyEnabled(false);
        }
        if (result.IsFailure)
        {
            DolphinData.SetEnabled(false);
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("status.recomp_dolphin_data_not_found"))
                .SetInfoText(result.Error.Message)
                .ShowDialog();
        }
        else
        {
            await ApplyNandSettingAsync();
        }
        LoadSettings();
    }

    /// <summary>
    /// The runtime reads <c>paths.nand_root</c> from its Config.toml at launch, so a changed sharing
    /// choice takes effect on the next launch without reinstalling anything.
    /// </summary>
    private async Task ApplyNandSettingAsync()
    {
        var dolphinData = DolphinData;
        if (dolphinData is null)
            return;

        var result = await Task.Run(dolphinData.ApplyNandToRecompConfig);
        if (result.IsFailure)
            MessageTranslationHelper.ShowMessage(result.Error);
    }

    private void OpenInstallFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var installFolder = RecompEnvironment?.InstallFolderPath ?? PathManager.RecompInstallFolderPath;
        if (Directory.Exists(installFolder))
            FilePickerHelper.OpenFolderInFileManager(installFolder);
    }

    private async void Uninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (RecompInstallService is null)
            return;

        // Shared Dolphin data lives in Dolphin's own Wii folder and survives; a copied or private
        // NAND belongs to the recomp and is removed with it, which the user must know upfront.
        var extraText = DolphinData is { IsEnabled: true, NandFolderPath: not null }
            ? DolphinData.CopyEnabled
                ? t("question.recomp_uninstall.extra_copy")
                : t("question.recomp_uninstall.extra_shared")
            : t("question.recomp_uninstall.extra_private");
        var confirmed = await new YesNoWindow()
            .SetMainText(t("question.recomp_uninstall.title"))
            .SetExtraText(extraText)
            .SetButtonText(t("action.un_install"), t("action.cancel"))
            .AwaitAnswer();
        if (!confirmed)
            return;

        IsEnabled = false;
        try
        {
            var result = await RecompInstallService.UninstallAsync();
            if (result.IsFailure)
            {
                MessageTranslationHelper.ShowMessage(result.Error);
                return;
            }

            ViewUtils.ShowSnackbar(t("status.recomp_uninstalled"));
        }
        finally
        {
            IsEnabled = true;
            LoadSettings();
        }
    }
}
