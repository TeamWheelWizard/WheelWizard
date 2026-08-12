using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups.Generic;

public partial class FirstTimeSetupPopup : PopupContent
{
    private enum SetupStep
    {
        Dolphin,
        GamePaths,
    }

    private readonly TaskCompletionSource<bool> _completionSource = new();
    private bool _setupCompleted;

    private SetupStep _currentStep = SetupStep.Dolphin;

    [Inject]
    private ILogger<FirstTimeSetupPopup> Logger { get; set; } = null!;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public bool WasSkipped { get; private set; }

    public FirstTimeSetupPopup()
        : base(true, false, false, "Wheel Wizard")
    {
        InitializeComponent();

        InitializeDolphinStep();
        ShowStep(SetupStep.Dolphin);
        AutoDetectPaths();
    }

    public Task<bool> ShowAndAwaitCompletionAsync()
    {
        Show();
        return _completionSource.Task;
    }

    private void ShowStep(SetupStep step)
    {
        _currentStep = step;

        DolphinStepPanel.IsVisible = step == SetupStep.Dolphin;
        GamePathsStepPanel.IsVisible = step == SetupStep.GamePaths;

        BackButton.IsVisible = step == SetupStep.GamePaths;
        SkipButton.IsVisible = step == SetupStep.GamePaths;

        UpdateContinueState();
    }

    private void UpdateContinueState()
    {
        ContinueButton.IsEnabled = _currentStep switch
        {
            SetupStep.Dolphin => IsDolphinStepComplete,
            SetupStep.GamePaths => IsGamePathsStepComplete,
            _ => false,
        };
        ErrorTextBlock.IsVisible = false;
    }

    private void ContinueButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentStep == SetupStep.Dolphin)
        {
            if (!IsDolphinStepComplete)
            {
                ShowError("Please select a Dolphin installation to continue.");
                return;
            }

            SaveDolphinStep();
            ShowStep(SetupStep.GamePaths);
            return;
        }

        if (!IsGamePathsStepComplete)
        {
            ShowError("Please select both the Dolphin user folder and your Mario Kart Wii game file.");
            return;
        }

        SaveGamePathsStep();
        CompleteSetup();
    }

    private void BackButton_OnClick(object? sender, RoutedEventArgs e) => ShowStep(SetupStep.Dolphin);

    private void SkipButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WasSkipped = true;
        CompleteSetup();
    }

    private void CompleteSetup()
    {
        _setupCompleted = true;
        _completionSource.TrySetResult(true);
        Close();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    protected override void BeforeClose() => _completionSource.TrySetResult(_setupCompleted);

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.IsVisible = true;
    }
}
