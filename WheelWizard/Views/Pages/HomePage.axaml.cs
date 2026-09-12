using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Testably.Abstractions;
using WheelWizard.Models.Enums;
using WheelWizard.Views.Components;

namespace WheelWizard.Views.Pages;

public partial class HomePage : UserControlBase
{
    private HomeViewModel Model { get; }

    private IRandomSystem RandomSystem { get; }

    private readonly WheelTrail[] _trails;
    private WheelTrailState _currentTrailState = WheelTrailState.Static_None;
    private bool _isAttached;

    public HomePage(HomeViewModel model, IRandomSystem randomSystem)
    {
        Model = model;
        RandomSystem = randomSystem;
        InitializeComponent();
        _trails = [HomeTrail1, HomeTrail2, HomeTrail3, HomeTrail4, HomeTrail5];
        RandomSystem.Random.Shared.Shuffle(_trails);
        DataContext = Model;
        if (Model.IsDolphinVisible)
            ApplyDolphinTrailColors();
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Model.PropertyChanged += Model_OnPropertyChanged;
        Model.MainActionStarted += Model_OnMainActionStarted;
        await Model.RefreshAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        Model.PropertyChanged -= Model_OnPropertyChanged;
        Model.MainActionStarted -= Model_OnMainActionStarted;
        Model.Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    private void Model_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.FindResource(Model.IconName) is Geometry geometry)
            PlayButton.IconData = geometry;
        if (Model.Status == WheelWizardStatus.Ready && !Model.IsBusy)
            PlayEntranceAnimation();
    }

    private void Model_OnMainActionStarted(object? sender, EventArgs e) => PlayActivateAnimation();

    private async void PlayButton_Click(object? sender, RoutedEventArgs e) => await Model.ExecuteMainAsync();

    private async void DolphinButton_OnClick(object? sender, RoutedEventArgs e) => await Model.LaunchDolphinAsync();

    private void ApplyDolphinTrailColors()
    {
        RecolorTrail(HomeTrail1, "Blue400", "Blue700");
        RecolorTrail(HomeTrail2, "Blue600", "Blue800");
        RecolorTrail(HomeTrail3, "Blue200", "Blue600");
        RecolorTrail(HomeTrail4, "Blue400", "Blue700");
        RecolorTrail(HomeTrail5, "Blue600", "Blue800");
    }

    private void RecolorTrail(WheelTrail trail, string backgroundKey, string foregroundKey)
    {
        if (this.FindResource(backgroundKey) is Color background)
            trail.Background = new SolidColorBrush(background);
        if (this.FindResource(foregroundKey) is Color foreground)
            trail.Foreground = new SolidColorBrush(foreground);
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
        if (!Model.AnimationsEnabled)
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
            if (!_isAttached)
                return;
            t.Classes.Add("EntranceTrail");
            await Task.Delay(80);
        }

        await Task.Delay(600);
        foreach (var t in _trails)
        {
            if (!_isAttached)
                return;
            t.Classes.Remove("EntranceTrail");
        }

        lock (_trails)
        {
            _currentTrailState = WheelTrailState.Static_Visible;
        }
    }

    private async void PlayActivateAnimation()
    {
        if (!Model.AnimationsEnabled)
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
            if (!_isAttached)
                return;
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
            if (!_isAttached)
                return;
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
            if (!_isAttached)
                return;
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
            if (!_isAttached)
                return;
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
            if (!_isAttached)
                return null;
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
}
