using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Launcher;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Pages.Settings;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Pages;

public partial class HomePage : UserControl
{
    private static ILauncher CurrentLauncher => LauncherTypes[s_launcherIndex];
    private static int s_launcherIndex; // Make sure this index never goes over the list index
    private static readonly List<ILauncher> LauncherTypes = [new RrLauncher()];

    private WheelWizardStatus _status;
    private MainButtonState CurrentButtonState => s_buttonStates[_status];

    private static readonly Dictionary<WheelWizardStatus, MainButtonState> s_buttonStates =
        new()
        {
            { WheelWizardStatus.Loading, new(Common.State_Loading, Button.ButtonsVariantType.Default, "Spinner", null, false) },
            { WheelWizardStatus.NoServer, new(Common.State_NoServer, Button.ButtonsVariantType.Danger, "RoadError", null, true) },
            {
                WheelWizardStatus.NoServerButInstalled,
                new(Common.Action_PlayOffline, Button.ButtonsVariantType.Warning, "Play", LaunchGame, true)
            },
            {
                WheelWizardStatus.NoDolphin,
                new("Dolphin not setup", Button.ButtonsVariantType.Warning, "Settings", NavigateToSettings, false)
            },
            {
                WheelWizardStatus.ConfigNotFinished,
                new(Common.State_ConfigNotFinished, Button.ButtonsVariantType.Warning, "Settings", NavigateToSettings, true)
            },
            { WheelWizardStatus.NotInstalled, new(Common.Action_Install, Button.ButtonsVariantType.Warning, "Download", Download, true) },
            { WheelWizardStatus.OutOfDate, new(Common.Action_Update, Button.ButtonsVariantType.Warning, "Download", Update, true) },
            { WheelWizardStatus.Ready, new(Common.Action_Play, Button.ButtonsVariantType.Primary, "Play", LaunchGame, true) }
        };


    public HomePage()
    {
        InitializeComponent();
        PopulateGameModeDropdown();
        UpdatePage();
    }

    private void UpdatePage()
    {
        GameTitle.Text = CurrentLauncher.GameTitle;
        UpdateActionButton();
    }

    private void DolphinButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DolphinLaunchHelper.LaunchDolphin();
        DisableAllButtonsTemporarily();
    }

    private static void LaunchGame() => CurrentLauncher.Launch();
    private static void NavigateToSettings() => ViewUtils.NavigateToPage(new SettingsPage());

    private static async void Download()
    {
        ViewUtils.GetLayout().SetInteractable(false);
        await CurrentLauncher.Install();
        ViewUtils.GetLayout().SetInteractable(true);
        ViewUtils.NavigateToPage(new HomePage());
    }

    private static async void Update()
    {
        ViewUtils.GetLayout().SetInteractable(false);
        await CurrentLauncher.Update();
        ViewUtils.GetLayout().SetInteractable(true);
        ViewUtils.NavigateToPage(new HomePage());
    }

    private void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        CurrentButtonState?.OnClick?.Invoke();

        UpdateActionButton();
        DisableAllButtonsTemporarily();
    }

    private void GameModeDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        s_launcherIndex = GameModeDropdown.SelectedIndex;
        UpdatePage();
    }

    private void PopulateGameModeDropdown()
    {
        // If there is only 1 option, we don't want to confuse the player with that option
        GameModeOption.IsVisible = LauncherTypes.Count > 1;
        if (!GameModeOption.IsVisible) return;

        foreach (var launcherType in LauncherTypes)
        {
            GameModeDropdown.Items.Add(launcherType.GameTitle);
        }

        GameModeDropdown.SelectedIndex = s_launcherIndex;
    }

    private async void UpdateActionButton()
    {
        _status = WheelWizardStatus.Loading;
        SetButtonState(CurrentButtonState);
        _status = await CurrentLauncher.GetCurrentStatus();
        SetButtonState(CurrentButtonState);
    }

    private void DisableAllButtonsTemporarily()
    {
        CompleteGrid.IsEnabled = false;
        //wait 5 seconds before re-enabling the buttons
        Task.Delay(2000).ContinueWith(_ =>
        {
            Dispatcher.UIThread.InvokeAsync(() => CompleteGrid.IsEnabled = true);
        });
    }

    private void SetButtonState(MainButtonState state)
    {
        PlayButton.Text = state.Text;
        PlayButton.Variant = state.Type;
        PlayButton.IsEnabled = state.OnClick != null;
        if (Application.Current != null && Application.Current.FindResource(state.IconName) is Geometry geometry)
            PlayButton.IconData = geometry;
        DolphinButton.IsEnabled = state.SubButtonsEnabled && SettingsHelper.PathsSetupCorrectly();
    }

    public class MainButtonState
    {
        public MainButtonState(string text, Button.ButtonsVariantType type, string iconName, Action? onClick, bool subButtonsEnables) =>
            (Text, Type, IconName, OnClick, SubButtonsEnabled) = (text, type, iconName, onClick, subButtonsEnables);

        public string Text { get; set; }
        public Button.ButtonsVariantType Type { get; set; }
        public string IconName { get; set; }
        public Action? OnClick { get; set; }
        public bool SubButtonsEnabled { get; set; }
    }
}
