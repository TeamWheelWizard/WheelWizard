using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Testably.Abstractions;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Launcher;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Components;
using WheelWizard.Views.Pages.Settings;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Pages;

public partial class HomePage : UserControlBase
{
    private static ILauncher currentLauncher => _launcherTypes[_launcherIndex];
    private static int _launcherIndex = 0; // Make sure this index never goes over the list index

    private WheelTrail[] _trails; // also used as a lock
    private WheelTrailState _currentTrailState = WheelTrailState.Static_None;

    private static List<ILauncher> _launcherTypes =
    [
        new RrLauncher(),
        //GoogleLauncher.Instance
    ];

    private WheelWizardStatus _status;
    private MainButtonState currentButtonState => buttonStates[_status];

    private static Dictionary<WheelWizardStatus, MainButtonState> buttonStates = new()
    {
        { WheelWizardStatus.Loading, new(Common.State_Loading, Button.ButtonsVariantType.Default, "Spinner", null, false) },
        { WheelWizardStatus.NoServer, new(Common.State_NoServer, Button.ButtonsVariantType.Danger, "RoadError", null, true) },
        {
            WheelWizardStatus.NoServerButInstalled,
            new(Common.Action_PlayOffline, Button.ButtonsVariantType.Warning, "Play", LaunchGame, true)
        },
        { WheelWizardStatus.NoDolphin, new("Dolphin not setup", Button.ButtonsVariantType.Warning, "Settings", NavigateToSettings, false) },
        {
            WheelWizardStatus.ConfigNotFinished,
            new(Common.State_ConfigNotFinished, Button.ButtonsVariantType.Warning, "Settings", NavigateToSettings, true)
        },
        { WheelWizardStatus.NotInstalled, new(Common.Action_Install, Button.ButtonsVariantType.Warning, "Download", Download, true) },
        { WheelWizardStatus.OutOfDate, new(Common.Action_Update, Button.ButtonsVariantType.Warning, "Download", Update, true) },
        { WheelWizardStatus.Ready, new(Common.Action_Play, Button.ButtonsVariantType.Primary, "Play", LaunchGame, true) },
    };

    public HomePage()
    {
        InitializeComponent();
        PopulateGameModeDropdown();
        UpdatePage();

        _trails = [HomeTrail1, HomeTrail2, HomeTrail3, HomeTrail4, HomeTrail5];
        App.Services.GetService<IRandomSystem>()?.Random.Shared.Shuffle(_trails);
        // We have to do it like `App.Service.GetService`. We cant make use of `private IRandomSystem Random { get; set; } = null!;` here
        // This is because this HomePage is always loaded first
    }

    private void UpdatePage()
    {
        GameTitle.Text = currentLauncher.GameTitle;
        UpdateActionButton();
    }

    private void DolphinButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DolphinLaunchHelper.LaunchDolphin();
        DisableAllButtonsTemporarily();
    }

    private static void LaunchGame() => currentLauncher.Launch();

    private static void NavigateToSettings() => NavigationManager.NavigateTo<SettingsPage>();

    private static async void Download()
    {
        ViewUtils.GetLayout().SetInteractable(false);
        await currentLauncher.Install();
        ViewUtils.GetLayout().SetInteractable(true);
        NavigationManager.NavigateTo<HomePage>();
    }

    private static async void Update()
    {
        ViewUtils.GetLayout().SetInteractable(false);
        await currentLauncher.Update();
        ViewUtils.GetLayout().SetInteractable(true);
        NavigationManager.NavigateTo<HomePage>();
    }

    private void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        currentButtonState?.OnClick?.Invoke();
        PlayActivateAnimation();
        UpdateActionButton();
        DisableAllButtonsTemporarily();
    }

    private void GameModeDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _launcherIndex = GameModeDropdown.SelectedIndex;
        UpdatePage();
    }

    private void PopulateGameModeDropdown()
    {
        // If there is only 1 option, we don't want to confuse the player with that option
        GameModeOption.IsVisible = _launcherTypes.Count > 1;
        if (!GameModeOption.IsVisible)
            return;

        foreach (var launcherType in _launcherTypes)
        {
            GameModeDropdown.Items.Add(launcherType.GameTitle);
        }

        GameModeDropdown.SelectedIndex = _launcherIndex;
    }

    private async void UpdateActionButton()
    {
        _status = WheelWizardStatus.Loading;
        SetButtonState(currentButtonState);
        _status = await currentLauncher.GetCurrentStatus();
        SetButtonState(currentButtonState);
    }

    private void DisableAllButtonsTemporarily()
    {
        CompleteGrid.IsEnabled = false;
        //wait 5 seconds before re-enabling the buttons
        Task.Delay(2000)
            .ContinueWith(_ =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SetButtonState(currentButtonState);
                    return CompleteGrid.IsEnabled = true;
                });
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

        if (_status == WheelWizardStatus.Ready)
            PlayEntranceAnimation();
    }

    #region WheelTrail Animations
    // --------------------------
    // IMPORTANT
    // --------------------------
    // When you are changing the animation, note that you are working with locks
    // Note that the enum _currentTrailState is used to determine the state of the wheel trails, and that should only  be read and changed under the influence of lock(_trails)
    // Also note that for NO REASON WHATSOEVER you are permitted to put other logic in these animation code other than the animation itself.
    // If for whatever reason the lock gets in to a deadlock, only the animation will freeze, the rest will continue to work.

    private async void PlayEntranceAnimation()
    {
        // If the animations are disabled, it will never play the entrance animation
        // The entrance animation is also the only one that makes the wheels visible, meaning hat if this one does not play
        // all the other animations are all also impossible to play
        if (!(bool)SettingsManager.ENABLE_ANIMATIONS.Get())
            return;

        var allowedToRun = WaitForWheelTrailState(
            WheelTrailState.Playing_Entrance,
            c => c is WheelTrailState.Static_None
        // even if there are 3 waiting, only 1 will go through, since there is an default check that it cant be the same
        );
        if (await allowedToRun == null)
            return;

        foreach (var t in _trails)
        {
            t.Classes.Add("EntranceTrail");
            await Task.Delay(80);
        }

        await Task.Delay(600);
        foreach (var t in _trails)
        {
            t.Classes.Remove("EntranceTrail");
        }

        lock (_trails)
        {
            _currentTrailState = WheelTrailState.Static_Visible;
        }
    }

    private async void PlayActivateAnimation()
    {
        if (!(bool)SettingsManager.ENABLE_ANIMATIONS.Get())
            return;

        var allowedToRun = WaitForWheelTrailState(
            WheelTrailState.Playing_Activate,
            c =>
                c
                    is WheelTrailState.Static_Hover
                        or WheelTrailState.Static_Visible
                        or WheelTrailState.Playing_HoverEnter
                        or WheelTrailState.Playing_HoverExit,
            c => c is WheelTrailState.Static_None or WheelTrailState.Playing_Activate
        );
        var oldState = await allowedToRun;
        if (oldState == null)
            return;

        foreach (var t in _trails)
        {
            t.Classes.Clear();
            if (oldState == WheelTrailState.Static_Hover)
                t.Classes.Add("ActivateTrailFromHover");
            else
                t.Classes.Add("ActivateTrailFromIdle");
            await Task.Delay(80);
        }

        await Task.Delay(1000);
        foreach (var t in _trails)
        {
            t.Classes.Remove("ActivateTrailFromIdle");
            t.Classes.Remove("ActivateTrailFromHover");
            await Task.Delay(40);
        }

        lock (_trails)
        {
            _currentTrailState = WheelTrailState.Static_None;
        }
    }

    private async void PlayButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        var allowedToRun = WaitForWheelTrailState(
            WheelTrailState.Playing_HoverEnter,
            c => c is WheelTrailState.Static_Visible or WheelTrailState.Playing_HoverExit,
            c => c is WheelTrailState.Playing_HoverExit
        );
        if (await allowedToRun == null)
            return;

        foreach (var t in _trails)
        {
            // Making sure that if after these seconds the state changed ,that it will not apply the class anymore
            lock (_trails)
            {
                if (_currentTrailState is not WheelTrailState.Playing_HoverEnter)
                    return;
            }

            t.Classes.Remove("HoverExitTrail");
            if (!t.Classes.Contains("HoverEnterTrail"))
                t.Classes.Add("HoverEnterTrail");
            await Task.Delay(20);
        }

        await Task.Delay(300);
        lock (_trails)
        {
            if (_currentTrailState is WheelTrailState.Playing_HoverEnter)
                _currentTrailState = WheelTrailState.Static_Hover;
        }
    }

    private async void PlayButton_OnPointerExit(object? sender, PointerEventArgs e)
    {
        var allowedToRun = WaitForWheelTrailState(
            WheelTrailState.Playing_HoverExit,
            c => c is WheelTrailState.Static_Hover or WheelTrailState.Playing_HoverEnter,
            c => c is not WheelTrailState.Static_Hover and not WheelTrailState.Playing_HoverEnter
        );
        if (await allowedToRun == null)
            return;

        foreach (var t in _trails)
        {
            lock (_trails)
            {
                if (_currentTrailState is not WheelTrailState.Playing_HoverExit)
                    return;
            }
            t.Classes.Remove("HoverEnterTrail");
            t.Classes.Add("HoverExitTrail");
        }

        await Task.Delay(350);
        lock (_trails)
        {
            if (_currentTrailState is WheelTrailState.Playing_HoverExit)
                _currentTrailState = WheelTrailState.Static_Visible;
        }
    }

    /// <summary>
    /// Easier way to wait for a specific animation state
    /// </summary>
    /// <param name="changeStateTo">the state that you are trying to set it to</param>
    /// <param name="acceptWhen">the states when it is allowed to override the state and continue the code</param>
    /// <param name="abortWhen">the statues when it should abort trying to set the state. it then also should not continue</param>
    /// <returns>null = aborted,  WheelTrailState = the old state that it was before the swap. This means success</returns>
    private async Task<WheelTrailState?> WaitForWheelTrailState(
        WheelTrailState changeStateTo,
        Func<WheelTrailState, bool> acceptWhen,
        Func<WheelTrailState, bool>? abortWhen = null
    )
    {
        bool accepted;
        WheelTrailState? oldState = null;
        lock (_trails)
        {
            accepted = acceptWhen(_currentTrailState);
            if (accepted)
            {
                oldState = _currentTrailState;
                _currentTrailState = changeStateTo;
            }
        }

        while (!accepted)
        {
            await Task.Delay(20);
            bool abort;
            lock (_trails)
            {
                abort = (abortWhen?.Invoke(_currentTrailState) ?? false) || _currentTrailState == changeStateTo;
            }
            if (abort)
                return null;

            lock (_trails)
            {
                accepted = acceptWhen(_currentTrailState);
                if (accepted)
                {
                    oldState = _currentTrailState;
                    _currentTrailState = changeStateTo;
                }
            }
        }

        return oldState;
    }

    enum WheelTrailState
    {
        Static_None, // It is not in view
        Static_Visible, // It is just doing nothing
        Static_Hover, // It is just doing nothing while it is being hovered

        Playing_Entrance, // Animation for entrance is playing              NOTHING is allowed to interrupt Playing_Entrance
        Playing_Activate, // Animation for activation is playing            NOTHING is allowed to interrupt Playing_Entrance
        Playing_HoverEnter, // Hover Enter animation is playing             can be interrupted
        Playing_HoverExit, // Hover Exit animation is exiting               can be interrupted
    }

    #endregion

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
