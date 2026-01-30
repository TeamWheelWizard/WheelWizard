using Avalonia.Interactivity;
using WheelWizard.CustomDistributions;
using WheelWizard.Models.Enums;
using WheelWizard.Services.Launcher;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Pages;

public partial class TestingPage : UserControlBase
{
    private readonly ILauncher _launcher;
    private WheelWizardStatus _status = WheelWizardStatus.Loading;
    private bool _isBusy;

    [Inject]
    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; set; } = null!;

    public TestingPage()
    {
        InitializeComponent();
        _launcher = new RrBetaLauncher();
        UpdateStatusAsync();
    }

    private async void UpdateStatusAsync()
    {
        _status = WheelWizardStatus.Loading;
        UpdateUi();

        _status = await _launcher.GetCurrentStatus();
        UpdateUi();
    }

    private void UpdateUi()
    {
        var pathsReady = SettingsHelper.PathsSetupCorrectly();

        InstallButton.IsEnabled = pathsReady && !_isBusy;
        DeleteButton.IsEnabled = !_isBusy && _status == WheelWizardStatus.Ready;
        LaunchButton.IsEnabled = !_isBusy && _status == WheelWizardStatus.Ready;
        DolphinButton.IsEnabled = !_isBusy && pathsReady;

        StatusText.Text = _status switch
        {
            WheelWizardStatus.ConfigNotFinished => "Setup required. Configure paths in Settings.",
            WheelWizardStatus.NotInstalled => "Not installed",
            WheelWizardStatus.Ready => "Installed",
            WheelWizardStatus.NoServer or WheelWizardStatus.NoServerButInstalled => "Server offline",
            WheelWizardStatus.OutOfDate => "Update available",
            _ => "Checking status...",
        };
    }

    private async void InstallButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        _isBusy = true;
        UpdateUi();

        await _launcher.Install();

        _isBusy = false;
        UpdateStatusAsync();
    }

    private async void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        _isBusy = true;
        UpdateUi();

        var progressWindow = new ProgressWindow("Removing test build");
        progressWindow.Show();
        var removeResult = await CustomDistributionSingletonService.RetroRewindBeta.RemoveAsync(progressWindow);
        progressWindow.Close();

        if (removeResult.IsFailure)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Unable to delete test build")
                .SetInfoText(removeResult.Error.Message)
                .ShowDialog();
        }

        _isBusy = false;
        UpdateStatusAsync();
    }

    private async void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        _isBusy = true;
        UpdateUi();
        await _launcher.Launch();
        _isBusy = false;
        UpdateUi();
    }

    private void DolphinButton_OnClick(object? sender, RoutedEventArgs e) => DolphinLaunchHelper.LaunchDolphin();
}
