using System.ComponentModel;
using Testably.Abstractions;
using WheelWizard.Launching;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Shared.Calendar;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Pages;

public sealed record HomeLaunchPrompt(string MainText, string ExtraText, string YesText, string NoText);

public interface IHomePresentation
{
    void ShowError(OperationError error);
    void OpenSettings();
    void SetApplicationInteractable(bool interactable);
    Task ShowLaunchPromptAsync(HomeLaunchPrompt prompt);
}

public sealed class HomeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILauncher _launcher;
    private readonly IDolphinLaunchService _dolphin;
    private readonly ISettingsManager _settings;
    private readonly IHomePresentation _presentation;
    private readonly ISeasonalCalendar _calendar;
    private readonly IRandomSystem _random;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _lifetime = new();
    private int _refreshGeneration;
    private bool _disposed;
    private bool _busy;
    private WheelWizardStatus _status = WheelWizardStatus.Loading;

    public HomeViewModel(
        ILauncherProvider launchers,
        IDolphinLaunchService dolphin,
        ISettingsManager settings,
        IHomePresentation presentation,
        ISeasonalCalendar calendar,
        IRandomSystem random,
        TimeProvider time
    )
    {
        _launcher = launchers.GetActiveLauncher();
        _dolphin = dolphin;
        _settings = settings;
        _presentation = presentation;
        _calendar = calendar;
        _random = random;
        _time = time;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MainActionStarted;
    public WheelWizardStatus Status => _status;
    public bool IsBusy => _busy;
    public bool IsInteractable => !_busy && !_disposed;
    public bool IsDolphinVisible => !_settings.IsRecompModeActive();
    public bool AnimationsEnabled => _settings.Get<bool>(_settings.ENABLE_ANIMATIONS);
    public string GameTitle => _launcher.GameTitle == "Retro Rewind" && _calendar.IsAprilFirst ? "Retro Beefbai" : _launcher.GameTitle;
    public bool CanExecuteMain =>
        IsInteractable
        && _status
            is WheelWizardStatus.NoServerButInstalled
                or WheelWizardStatus.NoDolphin
                or WheelWizardStatus.ConfigNotFinished
                or WheelWizardStatus.NotInstalled
                or WheelWizardStatus.OutOfDate
                or WheelWizardStatus.Ready;
    public bool CanLaunchDolphin =>
        IsInteractable
        && IsDolphinVisible
        && _settings.PathsSetupCorrectly()
        && _status is not (WheelWizardStatus.Loading or WheelWizardStatus.NoDolphin);

    public string ButtonText =>
        _status switch
        {
            WheelWizardStatus.NoServer => t("state.no_server"),
            WheelWizardStatus.NoServerButInstalled => t("action.play_offline"),
            WheelWizardStatus.NoDolphin => "Dolphin not setup",
            WheelWizardStatus.ConfigNotFinished => t("state.config_not_finished"),
            WheelWizardStatus.NotInstalled => t("action.install"),
            WheelWizardStatus.OutOfDate => t("action.update"),
            WheelWizardStatus.Ready => t("action.play"),
            _ => t("state.loading"),
        };

    public Button.ButtonsVariantType ButtonVariant =>
        _status switch
        {
            WheelWizardStatus.NoServer => Button.ButtonsVariantType.Danger,
            WheelWizardStatus.Ready => Button.ButtonsVariantType.Primary,
            WheelWizardStatus.Loading => Button.ButtonsVariantType.Default,
            _ => Button.ButtonsVariantType.Warning,
        };

    public string IconName =>
        _status switch
        {
            WheelWizardStatus.NoServer => "RoadError",
            WheelWizardStatus.NoServerButInstalled or WheelWizardStatus.Ready => "Play",
            WheelWizardStatus.NoDolphin or WheelWizardStatus.ConfigNotFinished => "Settings",
            WheelWizardStatus.NotInstalled or WheelWizardStatus.OutOfDate => "Download",
            _ => "Spinner",
        };

    public async Task RefreshAsync()
    {
        if (_disposed)
            return;
        var generation = ++_refreshGeneration;
        _status = WheelWizardStatus.Loading;
        NotifyStateChanged();
        WheelWizardStatus status;
        try
        {
            status = await _launcher.GetCurrentStatus();
        }
        catch (Exception ex)
        {
            status = WheelWizardStatus.NoServer;
            if (!_disposed && generation == _refreshGeneration)
                _presentation.ShowError(new OperationError { Message = "Failed to check game status.", Exception = ex });
        }
        if (_disposed || generation != _refreshGeneration)
            return;
        _status = status;
        NotifyStateChanged();
    }

    public async Task ExecuteMainAsync()
    {
        if (!CanExecuteMain)
            return;
        var action = _status;
        var setup = action is WheelWizardStatus.NotInstalled or WheelWizardStatus.OutOfDate;
        await RunActionAsync(
            async () =>
            {
                if (_calendar.IsAprilFirst && action is WheelWizardStatus.Ready or WheelWizardStatus.NoServerButInstalled)
                    await ShowAprilFirstPromptsAsync();
                MainActionStarted?.Invoke(this, EventArgs.Empty);
                if (action is WheelWizardStatus.NoDolphin or WheelWizardStatus.ConfigNotFinished)
                {
                    _presentation.OpenSettings();
                    return Ok();
                }
                return action switch
                {
                    WheelWizardStatus.NotInstalled => await _launcher.Install(),
                    WheelWizardStatus.OutOfDate => await _launcher.Update(),
                    _ => await _launcher.Launch(),
                };
            },
            setup
        );
    }

    public Task LaunchDolphinAsync() => CanLaunchDolphin ? RunActionAsync(() => _dolphin.LaunchDolphin(), false) : Task.CompletedTask;

    private async Task RunActionAsync(Func<Task<OperationResult>> action, bool setup)
    {
        _busy = true;
        ++_refreshGeneration;
        NotifyStateChanged();
        var started = _time.GetTimestamp();
        try
        {
            if (setup)
                _presentation.SetApplicationInteractable(false);
            var result = await action();
            if (result.IsFailure && !_disposed)
                _presentation.ShowError(result.Error);
        }
        catch (Exception ex)
        {
            if (!_disposed)
                _presentation.ShowError(new OperationError { Message = "The requested game operation failed.", Exception = ex });
        }
        finally
        {
            if (setup)
                _presentation.SetApplicationInteractable(true);
            var remaining = TimeSpan.FromSeconds(2) - _time.GetElapsedTime(started);
            try
            {
                if (remaining > TimeSpan.Zero && !_disposed)
                    await Task.Delay(remaining, _time, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_disposed) { }
            _busy = false;
            if (!_disposed)
                await RefreshAsync();
        }
    }

    private async Task ShowAprilFirstPromptsAsync()
    {
        HomeLaunchPrompt[] prompts =
        [
            new("You wanna start the game?", "This feels suspiciously productive.", "Yeah", "Nah"),
            new("Eeeeh not feeling like it", "Try asking a little nicer next time.", "Please", "Whatever"),
            new("Launch Retro Beefbai?", "I am consulting the ancient wheel.", "Do it", "Nope"),
            new("You again?", "The game is pretending not to notice you.", "Open it", "Leave it"),
            new("Starting the game already?", "That was fast. Almost too fast.", "Fine", "Hold on"),
        ];
        _random.Random.Shared.Shuffle(prompts);
        foreach (var prompt in prompts.Take(4))
            await _presentation.ShowLaunchPromptAsync(prompt);
    }

    private void NotifyStateChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ++_refreshGeneration;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
