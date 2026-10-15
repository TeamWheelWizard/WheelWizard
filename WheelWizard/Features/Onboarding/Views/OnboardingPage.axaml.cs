using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomDistributions;
using WheelWizard.DiscImages;
using WheelWizard.DolphinInstaller;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Domain;
using WheelWizard.Settings;
using WheelWizard.Shared.Desktop.Storage;
using WheelWizard.Shared.Processes;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Views;

namespace WheelWizard.Onboarding.Views;

/// <summary>
/// The first-run setup. Retro Rewind starts downloading the moment this page opens, while the user picks a
/// frontend, a Dolphin and the game. Until it finishes, the rest of the app stays out of reach.
/// </summary>
public partial class OnboardingPage : UserControl, IBlockingPage
{
    private enum Step
    {
        Choice,
        DolphinDetected,
        DolphinSource,
        DolphinMissingExtension,
        DolphinInstalling,
        DolphinImport,
        GameFile,
        NandChoice,
        WiiCompiledInstalling,
    }

    private enum Frontend
    {
        Dolphin,
        WiiCompiled,
    }

    private readonly IOnboardingService _onboarding;
    private readonly IRetroRewindBackgroundInstall _retroRewind;
    private readonly IManagedDolphinService _managedDolphin;
    private readonly IFilePickerService _filePicker;
    private readonly IDiscImageService _discImages;
    private readonly INavigationService _navigation;
    private readonly ISettingsManager _settings;
    private readonly ILogger<OnboardingPage> _logger;

    private readonly Stack<Step> _history = new();
    private Step _step;
    private Frontend _frontend = Frontend.Dolphin;
    private DetectedDolphin? _detectedDolphin;
    private DetectedDolphinData? _detectedData;
    private string? _gameFile;
    private bool _busy;

    // What the frontend choice looked like, so backing out to it undoes the path taken from there.
    private FrontendSettingsSnapshot? _choiceSettings;

    // The running install, which Back cancels. A cancelled one may still be unwinding, so the next waits for it.
    private CancellationTokenSource? _installCancellation;
    private Task _installRun = Task.CompletedTask;

    // Set while an install is in a part that cannot be stopped halfway (a Flatpak install, WiiCompiled's setup).
    private volatile bool _installUncancellable;

    public OnboardingPage(
        IOnboardingService onboarding,
        IRetroRewindBackgroundInstall retroRewind,
        IManagedDolphinService managedDolphin,
        IFilePickerService filePicker,
        IDiscImageService discImages,
        INavigationService navigation,
        ISettingsManager settings,
        ILogger<OnboardingPage> logger
    )
    {
        _settings = settings;
        _onboarding = onboarding;
        _retroRewind = retroRewind;
        _managedDolphin = managedDolphin;
        _filePicker = filePicker;
        _discImages = discImages;
        _navigation = navigation;
        _logger = logger;
        InitializeComponent();

        GameDropZone.AddHandler(DragDrop.DropEvent, GameDropZone_OnDrop);
        EnterCommandButton.IsVisible = OperatingSystem.IsLinux();
        ManagedDescriptionText.Text =
            _managedDolphin.Kind == ManagedDolphinKind.Flatpak
                ? t("onboarding.dolphin_managed_description_flatpak")
                : t("onboarding.dolphin_managed_description");
        ManagedTile.IsVisible = _managedDolphin.Kind is ManagedDolphinKind.Portable or ManagedDolphinKind.Flatpak;
        ExtensionCommandText.Text = "flatpak install flathub io.github.TeamWheelWizard.WheelWizard.Backend.DolphinEmu";

        _navigation.IsLocked = true;
        if (_onboarding.IsWiiCompiledAvailable)
            Show(Step.Choice);
        else
            _ = EnterDolphinPathAsync(remember: false);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _retroRewind.Changed += RetroRewind_OnChanged;
        UpdateRetroRewindStatus();
        // Retro Rewind is needed whichever way the user plays, so it downloads while they decide.
        _ = _retroRewind.StartAsync();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _retroRewind.Changed -= RetroRewind_OnChanged;
        base.OnDetachedFromVisualTree(e);
    }

    #region Steps

    private void GoTo(Step step)
    {
        _history.Push(_step);
        Show(step);
    }

    private void Show(Step step)
    {
        _step = step;
        ChoicePanel.IsVisible = step == Step.Choice;
        DetectedPanel.IsVisible = step == Step.DolphinDetected;
        SourcePanel.IsVisible = step == Step.DolphinSource;
        MissingExtensionPanel.IsVisible = step == Step.DolphinMissingExtension;
        ProgressPanel.IsVisible = step is Step.DolphinInstalling or Step.WiiCompiledInstalling;
        ImportPanel.IsVisible = step == Step.DolphinImport;
        GamePanel.IsVisible = step == Step.GameFile;
        NandPanel.IsVisible = step == Step.NandChoice;
        SourceErrorText.IsVisible = false;
        ImportErrorText.IsVisible = false;
        NandErrorText.IsVisible = false;

        (TitleText.Text, BodyText.Text) = step switch
        {
            Step.Choice => (t("onboarding.choice_title"), t("onboarding.choice_body")),
            Step.DolphinDetected => (t("onboarding.dolphin_detected_title"), t("onboarding.dolphin_body")),
            Step.DolphinSource => (t("onboarding.dolphin_title"), t("onboarding.dolphin_body")),
            Step.DolphinMissingExtension => (
                t("onboarding.dolphin_missing_extension_title"),
                t("onboarding.dolphin_missing_extension_body")
            ),
            Step.DolphinInstalling => (t("onboarding.dolphin_installing_title"), string.Empty),
            Step.DolphinImport => (t("onboarding.import_title"), t("onboarding.import_body")),
            Step.GameFile => (t("onboarding.game_title"), t("onboarding.game_body")),
            Step.NandChoice => (t("onboarding.nand_title"), t("onboarding.nand_body")),
            Step.WiiCompiledInstalling => (t("onboarding.wiicompiled_title"), string.Empty),
            _ => (string.Empty, string.Empty),
        };
        BodyText.IsVisible = !string.IsNullOrEmpty(BodyText.Text);

        if (step == Step.GameFile)
            ShowGameFile(_gameFile ?? ExistingGameFile());
        UpdateBackButton();
    }

    // An install is backed out of by cancelling it; other work (detecting, importing, copying) is short.
    private bool CanGoBack => _history.Count > 0 && (_installCancellation is not null ? !_installUncancellable : !_busy);

    private void UpdateBackButton() => BackButton.IsVisible = CanGoBack;

    private void Back_OnClick(object? sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (!CanGoBack)
            return;

        if (_installCancellation is { } install)
        {
            install.Cancel();
            _installCancellation = null;
            SetBusy(false);
        }

        var step = _history.Pop();
        if (step == Step.Choice && _choiceSettings is { } settings)
        {
            _onboarding.RestoreFrontendSettings(settings);
            _choiceSettings = null;
        }

        Show(step);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        IsEnabled = !busy || _step is Step.DolphinInstalling or Step.WiiCompiledInstalling;
        UpdateBackButton();
    }

    private void SetInstallUncancellable(bool uncancellable)
    {
        _installUncancellable = uncancellable;
        UpdateBackButton();
    }

    private void StartInstall(Func<CancellationToken, Task> install)
    {
        var cancellation = new CancellationTokenSource();
        _installCancellation = cancellation;
        _installRun = RunInstallAsync(_installRun, install, cancellation);
    }

    private async Task RunInstallAsync(Task previous, Func<CancellationToken, Task> install, CancellationTokenSource cancellation)
    {
        SetInstallUncancellable(false);
        SetBusy(true);
        StepRetryButton.IsVisible = false;
        SelectOwnInsteadButton.IsVisible = false;
        StepErrorText.IsVisible = false;
        try
        {
            await previous;
            cancellation.Token.ThrowIfCancellationRequested();
            await install(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Back cancelled it and already moved on.
        }
        catch (Exception exception) when (!cancellation.IsCancellationRequested)
        {
            _logger.LogError(exception, "An onboarding install failed");
            ShowStepError(exception.Message, allowOwnDolphin: _step == Step.DolphinInstalling);
        }
        finally
        {
            // Back already let go of a cancelled install, and a newer one may own the page by now.
            if (_installCancellation == cancellation)
            {
                _installCancellation = null;
                SetInstallUncancellable(false);
                SetBusy(false);
            }

            cancellation.Dispose();
        }
    }

    #endregion

    #region Choice

    private void ChooseDolphin_OnClick(object? sender, RoutedEventArgs e)
    {
        _choiceSettings = _onboarding.CaptureFrontendSettings();
        _frontend = Frontend.Dolphin;
        _ = EnterDolphinPathAsync(remember: true);
    }

    private void ChooseWiiCompiled_OnClick(object? sender, RoutedEventArgs e)
    {
        _choiceSettings = _onboarding.CaptureFrontendSettings();
        _frontend = Frontend.WiiCompiled;
        GoTo(Step.GameFile);
    }

    #endregion

    #region Dolphin

    private async Task EnterDolphinPathAsync(bool remember)
    {
        void Enter(Step step)
        {
            if (remember)
                GoTo(step);
            else
                Show(step);
        }

        if (_onboarding.IsMissingBundledDolphin)
        {
            Enter(Step.DolphinMissingExtension);
            return;
        }

        if (_onboarding.HasBundledDolphin)
        {
            // The Flatpak ships its own Dolphin, so there is nothing to choose.
            var result = _onboarding.UseBundledDolphin();
            if (result.IsFailure)
                _logger.LogWarning("Setting up the bundled Dolphin failed: {Message}", result.Error.Message);
            Enter(Step.GameFile);
            return;
        }

        // Asking a found Dolphin for its version starts a process, so keep it off the UI thread.
        SetBusy(true);
        try
        {
            _detectedDolphin = await Task.Run(_onboarding.DetectDolphin);
        }
        finally
        {
            SetBusy(false);
        }

        if (_detectedDolphin is not null)
        {
            DetectedVersionText.Text = _detectedDolphin.Version is { } version
                ? $"Dolphin · {t("onboarding.dolphin_detected_version", new { version })}"
                : "Dolphin";
            DetectedPathText.Text = _detectedDolphin.DisplayPath;
            Enter(Step.DolphinDetected);
            return;
        }

        Enter(Step.DolphinSource);
    }

    private void UseDetected_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_detectedDolphin is null)
            return;
        var result = _onboarding.UseOwnDolphin(_detectedDolphin.Location);
        if (result.IsFailure)
        {
            GoTo(Step.DolphinSource);
            ShowSourceError(result.Error.Message);
            return;
        }

        GoTo(Step.GameFile);
    }

    private void UseOtherDolphin_OnClick(object? sender, RoutedEventArgs e) => GoTo(Step.DolphinSource);

    private void ManagedDolphin_OnClick(object? sender, RoutedEventArgs e)
    {
        GoTo(Step.DolphinInstalling);
        StartInstall(InstallManagedDolphinAsync);
    }

    private async Task InstallManagedDolphinAsync(CancellationToken cancellationToken)
    {
        ShowProgress(0, t("onboarding.dolphin_downloading"));
        // Look for the user's other Dolphin before the install points the settings at the managed one.
        _detectedData ??= await Task.Run(_onboarding.DetectDolphinData, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        OperationResult result;
        // Coming back to this step after an install must not download Dolphin a second time.
        if (_managedDolphin.IsInstalled)
            result = _managedDolphin.Activate();
        else
        {
            // A Flatpak install cannot be stopped halfway.
            SetInstallUncancellable(_managedDolphin.Kind == ManagedDolphinKind.Flatpak);
            var progress = new Progress<ManagedDolphinProgress>(value =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    ShowProgress(value.Percent, value.Message);
            });
            result = await _managedDolphin.InstallAsync(progress, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailure)
        {
            ShowStepError($"{t("onboarding.dolphin_install_failed")} {result.Error.Message}", allowOwnDolphin: true);
            return;
        }

        // Replacing the install step keeps it out of the history, so Back returns to where Dolphin was picked.
        if (_detectedData is not null)
        {
            ImportPathText.Text = _detectedData.UserFolderPath;
            Show(Step.DolphinImport);
        }
        else
        {
            Show(Step.GameFile);
        }
    }

    private async void OwnDolphin_OnClick(object? sender, RoutedEventArgs e)
    {
        var location = await PickDolphinAsync();
        if (location is null)
            return;
        ApplyOwnDolphin(location);
    }

    private async void EnterCommand_OnClick(object? sender, RoutedEventArgs e)
    {
        var command = await new TextInputWindow()
            .SetMainText(t("onboarding.dolphin_enter_command"))
            .SetPlaceholderText($"Example: {ManagedDolphinService.FlatpakCommand}")
            .SetButtonText(t("action.cancel"), t("action.save"))
            .SetValidation((_, value) => string.IsNullOrWhiteSpace(value) ? Fail("Enter a launch command.") : Ok())
            .ShowDialog();
        if (!string.IsNullOrWhiteSpace(command))
            ApplyOwnDolphin(command.Trim());
    }

    private void ApplyOwnDolphin(string location)
    {
        var result = _onboarding.UseOwnDolphin(location);
        if (result.IsFailure)
        {
            ShowSourceError(result.Error.Message);
            return;
        }

        GoTo(Step.GameFile);
    }

    private async Task<string?> PickDolphinAsync()
    {
        if (OperatingSystem.IsMacOS())
        {
            var folders = await _filePicker.SelectFolderAsync("Select Dolphin.app");
            var app = folders.FirstOrDefault()?.TryGetLocalPath();
            return app is null ? null : ShellQuoting.QuoteUnixArgument(Path.Combine(app, "Contents", "MacOS", "Dolphin"));
        }

        var patterns = OperatingSystem.IsWindows() ? new[] { "*.exe" } : new[] { "*" };
        var file = await _filePicker.OpenSingleFileAsync("Select Dolphin Emulator", [new("Dolphin") { Patterns = patterns }]);
        if (string.IsNullOrWhiteSpace(file))
            return null;
        if (OperatingSystem.IsWindows())
        {
            if (string.Equals(Path.GetFileName(file), "DolphinTool.exe", StringComparison.OrdinalIgnoreCase))
            {
                ShowSourceError(t("onboarding.dolphin_invalid"));
                return null;
            }

            return file;
        }

        return ShellQuoting.QuoteUnixArgument(file);
    }

    // The managed install only starts from the source step, which is where the own Dolphin gets picked.
    private void SelectOwnInstead_OnClick(object? sender, RoutedEventArgs e) => GoBack();

    private void ShowSourceError(string message)
    {
        SourceErrorText.Text = message;
        SourceErrorText.IsVisible = true;
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_detectedData is null)
            return;
        SetBusy(true);
        ImportButton.IsLoading = true;
        try
        {
            var data = _detectedData;
            var result = await Task.Run(() => _managedDolphin.ImportUserData(data.WiiFolderPath, data.ConfigFolderPath));
            if (result.IsFailure)
            {
                ImportErrorText.Text = $"{t("onboarding.import_failed")} {result.Error.Message}";
                ImportErrorText.IsVisible = true;
                return;
            }

            GoTo(Step.GameFile);
        }
        finally
        {
            ImportButton.IsLoading = false;
            SetBusy(false);
        }
    }

    private void SkipImport_OnClick(object? sender, RoutedEventArgs e) => GoTo(Step.GameFile);

    #endregion

    #region Game file

    private string? ExistingGameFile()
    {
        var current = _settings.Get(_settings.GAME_LOCATION);
        return string.IsNullOrWhiteSpace(current) ? null : current;
    }

    private async void PickGame_OnClick(object? sender, RoutedEventArgs e)
    {
        var fileType = new FilePickerFileType("Game files")
        {
            Patterns = _discImages.SupportedExtensions.Select(extension => "*" + extension).ToArray(),
        };
        var file = await _filePicker.OpenSingleFileAsync("Select Mario Kart Wii Game File", [fileType]);
        if (!string.IsNullOrEmpty(file))
            ShowGameFile(file);
    }

    private void GameDropZone_OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(file))
            ShowGameFile(file);
    }

    private void ShowGameFile(string? file)
    {
        _gameFile = file;
        if (string.IsNullOrWhiteSpace(file))
        {
            GameResult.IsVisible = false;
            GameContinueButton.IsEnabled = false;
            return;
        }

        var check = _onboarding.CheckGameFile(file);
        GameResult.IsVisible = true;
        GameResultIcon.Data = Resource<Geometry>(check.IsValid ? "SuccessTip" : "ErrorTip");
        GameResultIcon.Foreground = new SolidColorBrush(Resource<Color>(check.IsValid ? "Brand300" : "Danger400"));
        GameResultText.Text = check.IsValid ? $"{check.Message}\n{Path.GetFileName(file)}" : check.Message;
        GameContinueButton.IsEnabled = check.IsValid;
    }

    private async void GameContinue_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_gameFile is null || _busy)
            return;
        var result = _onboarding.SetGameFile(_gameFile);
        if (result.IsFailure)
        {
            ShowGameFile(_gameFile);
            return;
        }

        if (_frontend == Frontend.Dolphin)
        {
            Finish(_onboarding.FinishDolphin());
            return;
        }

        if (_onboarding.HasDolphinNandToOffer)
        {
            GoTo(Step.NandChoice);
            return;
        }

        await ApplyNandChoiceAsync(RecompNandChoice.Private);
    }

    #endregion

    #region WiiCompiled

    private void NandShare_OnClick(object? sender, RoutedEventArgs e) => _ = ApplyNandChoiceAsync(RecompNandChoice.Share);

    private void NandCopy_OnClick(object? sender, RoutedEventArgs e) => _ = ApplyNandChoiceAsync(RecompNandChoice.Copy);

    private void NandFresh_OnClick(object? sender, RoutedEventArgs e) => _ = ApplyNandChoiceAsync(RecompNandChoice.Private);

    private async Task ApplyNandChoiceAsync(RecompNandChoice choice)
    {
        if (_busy)
            return;
        SetBusy(true);
        try
        {
            var result = await _onboarding.ApplyNandChoiceAsync(choice);
            if (result.IsFailure)
            {
                NandErrorText.Text = result.Error.Message;
                NandErrorText.IsVisible = true;
                return;
            }
        }
        finally
        {
            SetBusy(false);
        }

        GoTo(Step.WiiCompiledInstalling);
        StartInstall(InstallWiiCompiledAsync);
    }

    private async Task InstallWiiCompiledAsync(CancellationToken cancellationToken)
    {
        if (_retroRewind.State != BackgroundInstallState.Completed)
        {
            ShowProgress(_retroRewind.Percent, t("onboarding.wiicompiled_waiting"));
            // Backing out only stops the waiting; Retro Rewind keeps downloading for whichever path comes next.
            var download = await _retroRewind.StartAsync().WaitAsync(cancellationToken);
            if (download.IsFailure)
            {
                ShowStepError(_retroRewind.ErrorMessage ?? t("onboarding.rr_failed"), allowOwnDolphin: false);
                return;
            }
        }

        ShowProgress(0, t("onboarding.wiicompiled_title"));
        var progress = new InlineProgress<RecompInstallProgress>(value =>
        {
            // From here the setup rewrites installed files, so stopping it would leave a broken install. This runs on
            // the installer's own thread, before the setup starts, so Back is gone by the time it could do harm.
            if (!cancellationToken.IsCancellationRequested && value.Percent >= RecompSetupHostAcquirer.SetupPercentFloor)
                _installUncancellable = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                UpdateBackButton();
                ShowProgress(value.Percent, value.Message);
            });
        });
        var result = await _onboarding.FinishWiiCompiledAsync(progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailure)
        {
            ShowStepError($"{t("onboarding.wiicompiled_failed")} {result.Error.Message}", allowOwnDolphin: false);
            return;
        }

        Finish(Ok());
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    #endregion

    #region Progress and finishing

    private void ShowProgress(int percent, string message)
    {
        StepProgressBar.Value = Math.Clamp(percent, 0, 100);
        StepProgressText.Text = message;
    }

    private void ShowStepError(string message, bool allowOwnDolphin)
    {
        StepErrorText.Text = message;
        StepErrorText.IsVisible = true;
        StepRetryButton.IsVisible = true;
        SelectOwnInsteadButton.IsVisible = allowOwnDolphin;
    }

    private void StepRetry_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (_step == Step.DolphinInstalling)
            StartInstall(InstallManagedDolphinAsync);
        else if (_step == Step.WiiCompiledInstalling)
        {
            if (_retroRewind.State == BackgroundInstallState.Failed)
                _ = _retroRewind.RetryAsync();
            StartInstall(InstallWiiCompiledAsync);
        }
    }

    private void Finish(OperationResult result)
    {
        if (result.IsFailure)
        {
            _logger.LogError("Finishing the onboarding failed: {Message}", result.Error.Message);
            return;
        }

        _navigation.IsLocked = false;
        _navigation.NavigateAway(typeof(HomePage));
        if (ViewUtils.GetLayout() is { } layout)
        {
            layout.UpdateSidebarProfile();
            layout.UpdateFriendCount();
        }
    }

    #endregion

    #region Retro Rewind status

    private void RetroRewind_OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateRetroRewindStatus);

    private void UpdateRetroRewindStatus()
    {
        var state = _retroRewind.State;
        RetroRewindStatus.IsVisible = state != BackgroundInstallState.Idle;
        RetroRewindSpinner.IsVisible = state == BackgroundInstallState.Running;
        RetroRewindIcon.IsVisible = state != BackgroundInstallState.Running;
        RetroRewindRetryButton.IsVisible = state == BackgroundInstallState.Failed;
        var failed = state == BackgroundInstallState.Failed;
        RetroRewindIcon.Data = Resource<Geometry>(failed ? "WarningTip" : "SuccessTip");
        RetroRewindIcon.Foreground = new SolidColorBrush(Resource<Color>(failed ? "Warning400" : "Brand300"));
        RetroRewindText.Text = state switch
        {
            BackgroundInstallState.Running when _retroRewind.Message == t("state.extracting") => t(
                "onboarding.rr_extracting",
                new { percent = _retroRewind.Percent }
            ),
            BackgroundInstallState.Running => t("onboarding.rr_downloading", new { percent = _retroRewind.Percent }),
            BackgroundInstallState.Failed => t("onboarding.rr_failed"),
            BackgroundInstallState.Completed => t("onboarding.rr_ready"),
            _ => string.Empty,
        };

        if (_step == Step.WiiCompiledInstalling && state == BackgroundInstallState.Running)
            ShowProgress(_retroRewind.Percent, t("onboarding.wiicompiled_waiting"));
    }

    private void RetroRewindRetry_OnClick(object? sender, RoutedEventArgs e) => _ = _retroRewind.RetryAsync();

    #endregion

    private T Resource<T>(string key) => (T)this.FindResource(key)!;
}
