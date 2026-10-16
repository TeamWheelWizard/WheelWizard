using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using WheelWizard.DiscImages;
using WheelWizard.Settings.Views;
using WheelWizard.Shared.Desktop.Storage;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Views;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.GameFiles.Views;

/// <summary>
/// Shown instead of Home while the extracted game is not usable: someone coming from a version that played the image
/// directly, an image that can't be found, a changed main.dol or StaticR.rel, or a folder in the way. Only Settings
/// stays reachable, to free up or move the data folder.
/// </summary>
public partial class GameSetupPage : UserControl, IBlockingPage
{
    private readonly IGameFilesService _gameFiles;
    private readonly IGameFilesFlow _flow;
    private readonly IFilePickerService _filePicker;
    private readonly IDiscImageService _discImages;
    private readonly INavigationService _navigation;
    private readonly ILogger<GameSetupPage> _logger;

    private GameFilesStatus _status = new(GameFilesState.NoImage, null, null);
    private CancellationTokenSource? _extraction;
    private bool _busy;

    public GameSetupPage(
        IGameFilesService gameFiles,
        IGameFilesFlow flow,
        IFilePickerService filePicker,
        IDiscImageService discImages,
        INavigationService navigation,
        ILogger<GameSetupPage> logger
    )
    {
        _gameFiles = gameFiles;
        _flow = flow;
        _filePicker = filePicker;
        _discImages = discImages;
        _navigation = navigation;
        _logger = logger;
        InitializeComponent();
        _navigation.IsLocked = true;
        Refresh();
    }

    /// <summary>Whether the game needs this page before anything can be played.</summary>
    public static bool IsNeeded(GameFilesStatus status) => status.State is not (GameFilesState.Ready or GameFilesState.NoImage);

    private void Refresh()
    {
        _gameFiles.RecoverInterruptedWork();
        _status = _gameFiles.GetStatus();
        if (!IsNeeded(_status))
        {
            Finish();
            return;
        }

        var (title, body) = _status.State switch
        {
            GameFilesState.ImageMissing => (t("game_files.missing_title"), t("game_files.missing_body")),
            GameFilesState.CodeModified => (t("game_files.modified_title"), t("game_files.modified_body")),
            GameFilesState.UnknownFolder => (t("game_files.unknown_folder_title"), t("game_files.unknown_folder_body")),
            _ => (t("game_files.setup_title"), t("game_files.setup_body")),
        };
        TitleText.Text = title;
        BodyText.Text = body;
        ShowGame();

        var hasImage = _status.ImagePath is { } image && File.Exists(image);
        (PrimaryButton.Text, PrimaryButton.IsVisible) = _status.State switch
        {
            GameFilesState.NeedsExtraction => (t("game_files.extract_action"), true),
            GameFilesState.ImageMissing => (t("game_files.pick_game"), true),
            GameFilesState.CodeModified => hasImage ? (t("game_files.restore_action"), true) : (t("game_files.pick_game"), true),
            GameFilesState.UnknownFolder => (t("game_files.move_aside"), true),
            _ => (string.Empty, false),
        };
        SecondaryButton.Text = t("game_files.delete");
        SecondaryButton.IsVisible = _status.State == GameFilesState.UnknownFolder;
        PickOtherButton.IsVisible =
            _status.State is GameFilesState.NeedsExtraction || (_status.State == GameFilesState.CodeModified && hasImage);
        SetBusy(false);
    }

    private void ShowGame()
    {
        var image = _status.ImagePath;
        GameBox.IsVisible = image is not null || _status.Manifest is not null;
        var gameId =
            _status.Manifest?.GameId
            ?? (image is not null && _discImages.ReadInfo(image) is { IsSuccess: true } info ? info.Value.GameId : null);
        var region = GameRegion.FromGameId(gameId);
        GameNameText.Text =
            region == Models.Enums.MarioKartWiiEnums.Regions.None
                ? "Mario Kart Wii"
                : $"Mario Kart Wii · {GameRegion.GetDisplayName(region)}";
        GamePathText.Text = image ?? string.Empty;
        GamePathText.IsVisible = image is not null;
        var reachable = image is not null && File.Exists(image);
        // The page is filled in while it is built, before it is in a window whose theme it could look in.
        if (this.TryFindResource(reachable ? "SuccessTip" : "WarningTip", out var icon) && icon is Geometry geometry)
            GameIcon.Data = geometry;
        if (this.TryFindResource(reachable ? "Brand300" : "Warning400", out var tone) && tone is Color color)
            GameIcon.Foreground = new SolidColorBrush(color);
    }

    private void SetBusy(bool busy, bool extracting = false)
    {
        _busy = busy;
        ActionsPanel.IsEnabled = !busy;
        ActionsPanel.IsVisible = !extracting;
        ProgressPanel.IsVisible = extracting;
        if (!busy)
            ProgressPanel.IsVisible = false;
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? string.Empty;
        ErrorText.IsVisible = !string.IsNullOrEmpty(message);
    }

    private async void Primary_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        ShowError(null);
        switch (_status.State)
        {
            case GameFilesState.NeedsExtraction:
                await ExtractAsync(progress => _flow.ExtractCurrentAsync(progress, _extraction!.Token));
                break;
            case GameFilesState.ImageMissing:
                await PickAndUseAsync();
                break;
            case GameFilesState.CodeModified when _status.ImagePath is { } image && File.Exists(image):
                await RestoreAsync(image);
                break;
            case GameFilesState.CodeModified:
                await PickAndUseAsync();
                break;
            case GameFilesState.UnknownFolder:
                await RunBusyAsync(async () =>
                {
                    var moved = await Task.Run(_gameFiles.MoveUnknownAside);
                    if (moved.IsFailure)
                        ShowError(moved.Error.Message);
                });
                break;
        }

        Refresh();
    }

    private async void Secondary_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _status.State != GameFilesState.UnknownFolder)
            return;
        ShowError(null);
        await RunBusyAsync(() => _flow.ResolveUnknownFolderAsync());
        Refresh();
    }

    private async void PickOther_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        ShowError(null);
        await PickAndUseAsync();
        Refresh();
    }

    private async Task PickAndUseAsync()
    {
        var fileType = new FilePickerFileType("Game files")
        {
            Patterns = _discImages.SupportedExtensions.Select(extension => "*" + extension).ToArray(),
        };
        var file = await _filePicker.OpenSingleFileAsync("Select Mario Kart Wii Game File", [fileType]);
        if (string.IsNullOrEmpty(file))
            return;

        if (_status.State == GameFilesState.CodeModified)
            await RestoreAsync(file);
        else
            await ExtractAsync(progress => _flow.ChangeImageAsync(file, progress, _extraction!.Token));
    }

    private async Task RestoreAsync(string image)
    {
        await RunBusyAsync(async () =>
        {
            if (!await _flow.WaitForGameClosedAsync())
                return;
            var restored = await _gameFiles.RestoreCodeAsync(image);
            if (restored.IsFailure)
                ShowError(restored.Error.Message);
        });
    }

    private async Task ExtractAsync(Func<IProgress<GameExtractionProgress>, Task<bool>> extract)
    {
        _extraction = new CancellationTokenSource();
        SetBusy(true, extracting: true);
        CancelButton.IsEnabled = true;
        ExtractProgressBar.Value = 0;
        ProgressText.Text = t("game_files.extracting");
        var progress = new Progress<GameExtractionProgress>(value =>
        {
            ExtractProgressBar.Value = Math.Clamp(value.Fraction * 100, 0, 100);
            ProgressText.Text = $"{value.Message} · {(int)(value.Fraction * 100)}%";
        });
        try
        {
            await extract(progress);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Preparing the game files failed");
            ShowError(exception.Message);
        }
        finally
        {
            _extraction.Dispose();
            _extraction = null;
            SetBusy(false);
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        SetBusy(true);
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A game setup action failed");
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        _extraction?.Cancel();
    }

    private void OpenSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        // Settings is the one page reachable from here; leaving it for anything else comes back to this page.
        _navigation.IsLocked = false;
        _navigation.NavigateAway(typeof(SettingsPage));
    }

    private void Finish()
    {
        _navigation.IsLocked = false;
        _navigation.NavigateAway(typeof(HomePage));
        if (ViewUtils.GetLayout() is { } layout)
        {
            layout.UpdateSidebarProfile();
            layout.UpdateFriendCount();
        }
    }
}
