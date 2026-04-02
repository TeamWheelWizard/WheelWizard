using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Serilog;
using WheelWizard.Converters;
using WheelWizard.Helpers;
using WheelWizard.Models;
using WheelWizard.Services;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Shared.DependencyInjection;

namespace WheelWizard.Views.Pages;

public partial class GhostTimesPage : UserControlBase, INotifyPropertyChanged
{
    [Inject] private GhostTrackService _ghostTrackService { get; set; } = null!;
    [Inject] private GhostLeaderboardService _leaderboardService { get; set; } = null!;
    [Inject] private LocalGhostService _localGhostService { get; set; } = null!;
    [Inject] private TrackHexMappingService _trackHexMappingService { get; set; } = null!;
    private GhostTrack? _selectedTrack;
    private bool _isOnlineTabSelected = true;
    private bool _isLoading = false;
    private bool _isLoadingLocalGhosts = false;
    private CancellationTokenSource? _loadingCancellationTokenSource;
    private GhostTrackInfo? _currentTrackInfo;
    private LocalTrackGhosts? _currentLocalTrackGhosts;

    public ObservableCollection<GhostSubmission> Submissions { get; } = [];
    public ObservableCollection<LocalGhostData> LocalGhosts { get; } = [];

    public bool IsOnlineTabSelected
    {
        get => _isOnlineTabSelected;
        set
        {
            if (_isOnlineTabSelected != value)
            {
                _isOnlineTabSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOnlineTabSelected)));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
                UpdateUI();
            }
        }
    }

    public bool IsLoadingLocalGhosts
    {
        get => _isLoadingLocalGhosts;
        set
        {
            if (_isLoadingLocalGhosts != value)
            {
                _isLoadingLocalGhosts = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoadingLocalGhosts)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoLocalGhosts)));
            }
        }
    }

    public string LocalGhostsSummary
    {
        get
        {
            if (_currentLocalTrackGhosts?.HasGhosts == true)
            {
                var total = _currentLocalTrackGhosts.TotalGhostCount;
                var cc150Count = _currentLocalTrackGhosts.Ghosts150.Count + _currentLocalTrackGhosts.VariantGhosts150.Count;
                var cc200Count = _currentLocalTrackGhosts.Ghosts200.Count + _currentLocalTrackGhosts.VariantGhosts200.Count;
                return $"{total} ghost files found ({cc150Count} at 150cc, {cc200Count} at 200cc)";
            }
            return "No local ghost files found for this track";
        }
    }

    public bool HasNoLocalGhosts => !IsLoadingLocalGhosts && LocalGhosts.Count == 0;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public GhostTimesPage() 
    {
        InitializeComponent();
        DataContext = this;
    }

    public GhostTimesPage(GhostTrack track)
    {
        InitializeComponent();
        DataContext = this;
        
        SetTrack(track);
        UpdateUI();
    }

    public void SetTrack(GhostTrack track)
    {
        _selectedTrack = track;
        
        if (_selectedTrack != null)
        {
            TrackTitle.Text = string.IsNullOrEmpty(_selectedTrack.Console) 
                ? _selectedTrack.Name
                : $"{_selectedTrack.Console} {_selectedTrack.Name}";
                
            if (IsOnlineTabSelected)
            {
                _ = LoadOnlineLeaderboardSafe();
            }
        }
    }

    private void UpdateUI()
    {
        LoadingText.IsVisible = IsLoading;
        EmptyText.IsVisible = !IsLoading && Submissions.Count == 0 && IsOnlineTabSelected;
        LeaderboardListBox.IsVisible = !IsLoading && Submissions.Count > 0;
        RefreshButton.IsEnabled = !IsLoading;
    }

    private async void FilterChanged(object? sender, RoutedEventArgs e)
    {
        if (IsOnlineTabSelected && _selectedTrack != null && !IsLoading)
        {
            await Task.Delay(100);
            await LoadOnlineLeaderboardSafe();
        }
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsOnlineTabSelected && _selectedTrack != null)
        {
            await LoadOnlineLeaderboardSafe();
        }
    }

    private async Task LoadOnlineLeaderboardSafe()
    {
        _loadingCancellationTokenSource?.Cancel();
        _loadingCancellationTokenSource = new CancellationTokenSource();
        
        try
        {
            await LoadOnlineLeaderboard(_loadingCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Leaderboard loading was cancelled");
        }
    }

    private async Task LoadOnlineLeaderboard(CancellationToken cancellationToken = default)
    {
        if (_selectedTrack == null) return;

        try
        {
            IsLoading = true;
            Submissions.Clear();

            cancellationToken.ThrowIfCancellationRequested();

            var trackId = _selectedTrack.TrackId;
            if (trackId <= 0)
            {
                Log.Warning("Invalid track ID in track: {TrackName}, TrackId: {TrackId}", _selectedTrack.Name, trackId);
                return;
            }

            var glitchAllowed = GlitchAllowedRadio.IsChecked == true;
            var cc = Cc150Radio.IsChecked == true ? GhostCc.Cc150 : GhostCc.Cc200;
            var leaderboardType = FlapTypeRadio.IsChecked == true ? GhostLeaderboardType.Flap : GhostLeaderboardType.Regular;

            Log.Information("Loading leaderboard for track {TrackName} (ID: {TrackId}) - CC: {CC}, Glitch: {Glitch}, Type: {Type}",
                _selectedTrack.Name, trackId, cc, glitchAllowed, leaderboardType);

            cancellationToken.ThrowIfCancellationRequested();

            var leaderboard = await _leaderboardService.GetLeaderboardAsync(trackId, glitchAllowed, cc, leaderboardType);
            
            cancellationToken.ThrowIfCancellationRequested();
            
            if (leaderboard?.Submissions != null)
            {
                _currentTrackInfo = leaderboard.Track;
                
                foreach (var submission in leaderboard.Submissions)
                {
                    Submissions.Add(submission);
                }
                
                Log.Information("Loaded {Count} submissions for track {TrackName}", 
                    Submissions.Count, _selectedTrack.Name);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading online leaderboard for track {TrackName}", _selectedTrack?.Name);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void DownloadGhostButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GhostSubmission submission)
        {
            try
            {
                Log.Information("Requesting download for ghost: {PlayerName} - {Time}", 
                    submission.PlayerName, submission.FinishTimeDisplay);
                
                var confirmDialog = new Views.Popups.Generic.YesNoWindow()
                    .SetMainText($"Download ghost from {submission.PlayerName}?")
                    .SetExtraText($"Time: {submission.FinishTimeDisplay}\nTrack: {submission.TrackName}")
                    .SetButtonText("Download", "Cancel");
                
                var shouldDownload = await confirmDialog.AwaitAnswer();
                if (!shouldDownload)
                {
                    Log.Debug("Ghost download cancelled by user");
                    return;
                }
                
                var hexValue = _trackHexMappingService.GetHexValueForTrack(submission.TrackName);
                if (string.IsNullOrEmpty(hexValue))
                {
                    Log.Warning("No hex value found for track: {TrackName}", submission.TrackName);
                    return;
                }
                
                var downloadUrl = $"https://rwfc.net/api/timetrial/ghost/{submission.Id}/download";
                
                var ccFolder = submission.Cc.ToString();
                var targetFolder = Path.Combine(PathManager.GhostsFolderPath, hexValue.ToLowerInvariant(), ccFolder);
                var placeholderFilePath = Path.Combine(targetFolder, "placeholder.rkg"); // Ensure .rkg extension
                
                Directory.CreateDirectory(targetFolder);
                
                Log.Information("Downloading ghost from {Url} to folder {FolderPath}", downloadUrl, targetFolder);
                
                var downloadedPath = await DownloadHelper.DownloadToLocationAsync(
                    downloadUrl,
                    placeholderFilePath,
                    "Downloading Ghost",
                    $"Downloading {submission.PlayerName}'s ghost...",
                    false 
                );
                
                if (!string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath) && !downloadedPath.EndsWith(".rkg", StringComparison.OrdinalIgnoreCase))
                {
                    var rkgPath = Path.ChangeExtension(downloadedPath, ".rkg");
                    File.Move(downloadedPath, rkgPath);
                    downloadedPath = rkgPath;
                    Log.Information("Renamed downloaded file to ensure .rkg extension: {RkgPath}", rkgPath);
                }
                
                if (!string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath))
                {
                    Log.Information("Ghost downloaded successfully to: {FilePath}", downloadedPath);
                    
                    if (_currentTrackInfo?.Name == submission.TrackName)
                    {
                        LoadLocalGhosts();
                    }
                }
                else
                {
                    Log.Warning("Ghost download failed or file not found");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error downloading ghost file for player: {PlayerName}", submission.PlayerName);
            }
        }
    }

    private void DetailsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GhostSubmission submission)
        {
            try
            {
                var detailsWindow = new Views.Popups.GhostDetailsWindow(submission, _currentTrackInfo);
                var parentWindow = TopLevel.GetTopLevel(this) as Window;
                if (parentWindow != null)
                {
                    detailsWindow.ShowDialog(parentWindow);
                }
                else
                {
                    detailsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error opening ghost details for player {PlayerName}", submission.PlayerName);
            }
        }
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        NavigationManager.NavigateTo<GhostsPage>();
    }

    private async void LoadLocalGhosts()
    {
        if (_selectedTrack == null) 
        {
            Log.Warning("Cannot load local ghosts: no track selected");
            return;
        }

        try
        {
            IsLoadingLocalGhosts = true;
            LocalGhosts.Clear();
            Log.Information("Loading local ghosts for track {TrackName} (HexValue: {HexValue})", _selectedTrack.Name, _selectedTrack.HexValue);

            if (uint.TryParse(_selectedTrack.HexValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var trackId))
            {
                _currentLocalTrackGhosts = await _localGhostService.GetTrackGhostsAsync(trackId, _selectedTrack.HexValue);
                
                if (_currentLocalTrackGhosts?.HasGhosts == true)
                {
                    Log.Information("Found {GhostCount} local ghosts for track {TrackName}", _currentLocalTrackGhosts.TotalGhostCount, _selectedTrack.Name);
                    LoadGhostsForSelectedCc();
                }
                else
                {
                    Log.Information("No local ghosts found for track {TrackName}", _selectedTrack.Name);
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalGhostsSummary)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoLocalGhosts)));
            }
            else
            {
                Log.Warning("Could not parse track ID from HexValue: {HexValue} for track: {TrackName}", _selectedTrack.HexValue, _selectedTrack.Name);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading local ghosts for track: {TrackName}", _selectedTrack?.Name);
        }
        finally
        {
            IsLoadingLocalGhosts = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoadingLocalGhosts)));
        }
    }

    private void LoadGhostsForSelectedCc()
    {
        if (_currentLocalTrackGhosts == null) 
        {
            Log.Warning("Cannot load ghosts for selected CC: no current local track ghosts");
            return;
        }

        LocalGhosts.Clear();

        var is150cc = Local150Radio?.IsChecked == true;
        var ghostsToLoad = is150cc 
            ? _currentLocalTrackGhosts.Ghosts150.Concat(_currentLocalTrackGhosts.VariantGhosts150)
            : _currentLocalTrackGhosts.Ghosts200.Concat(_currentLocalTrackGhosts.VariantGhosts200);

        var ghostList = ghostsToLoad.OrderBy(g => g.TotalTimeMs).ToList();
        Log.Information("Loading {GhostCount} ghosts for {CC}cc", ghostList.Count, is150cc ? 150 : 200);

        for (int i = 0; i < ghostList.Count; i++)
        {
            ghostList[i].Rank = i + 1;
            LocalGhosts.Add(ghostList[i]);
        }
        
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalGhosts)));
    }

    private void TabChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton)
        {
            if (radioButton == OnlineTabRadio)
            {
                IsOnlineTabSelected = true;
                Log.Information("Tab changed to: Online");
            }
            else if (radioButton == OfflineTabRadio)
            {
                IsOnlineTabSelected = false;
                Log.Information("Tab changed to: Local Saves");
                Log.Information("Loading local ghosts for track: {TrackName}", _selectedTrack?.Name);
                LoadLocalGhosts();
            }
        }
    }

    private void LocalCcChanged(object? sender, RoutedEventArgs e)
    {
        LoadGhostsForSelectedCc();
        _localGhostService.RefreshGhostData();
        LoadLocalGhosts();
    }

    private void RefreshLocalGhosts_Click(object? sender, RoutedEventArgs e)
    {
        _localGhostService.RefreshGhostData();
        LoadLocalGhosts();
    }

    private void ShowGhostFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedTrack != null)
        {
            _localGhostService.OpenTrackFolderInExplorer(_selectedTrack.HexValue);
        }
    }

    private async void DeleteGhostFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is LocalGhostData ghost)
        {
            try
            {
                var fileName = Path.GetFileName(ghost.FilePath);
                var confirmDialog = new YesNoWindow()
                    .SetMainText("Delete Ghost File")
                    .SetExtraText($"Are you sure you want to delete '{fileName}'?\n\nThis action cannot be undone.")
                    .SetButtonText("Delete", "Cancel");

                var result = await confirmDialog.AwaitAnswer();
                
                if (result)
                {
                    if (File.Exists(ghost.FilePath))
                    {
                        File.Delete(ghost.FilePath);
                        Log.Information("Deleted ghost file: {FilePath}", ghost.FilePath);
                        
                        LocalGhosts.Remove(ghost);
                        _localGhostService.RefreshGhostData();
                        LoadLocalGhosts();
                    }
                    else
                    {
                        Log.Warning("Ghost file not found: {FilePath}", ghost.FilePath);
                        _localGhostService.RefreshGhostData();
                        LoadLocalGhosts();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting ghost file: {FilePath}", ghost.FilePath);
                
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Error)
                    .SetTitleText("Error")
                    .SetInfoText($"Failed to delete ghost file:\n{ex.Message}")
                    .Show();
            }
        }
    }

    private void LocalGhostDetails_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is LocalGhostData ghost)
        {
            try
            {
                var submission = new GhostSubmission
                {
                    PlayerName = ghost.MiiData,
                    MiiName = ghost.MiiData,
                    FinishTimeMs = (int)ghost.TotalTimeMs,
                    FinishTimeDisplay = ghost.TotalTimeDisplay,
                    LapSplitsMs = ghost.LapSplitsMs.Select(l => (int)l).ToList(),
                    LapSplitsDisplay = ghost.LapSplitsDisplay,
                    FastestLapMs = (int)ghost.FastestLapMs,
                    FastestLapDisplay = ghost.FastestLapDisplay,
                    CharacterId = ghost.CharacterId,
                    VehicleId = ghost.VehicleId,
                    ControllerType = ghost.ControllerType,
                    CountryCode = ghost.Country,
                    DateSet = ghost.Date.ToString("yyyy-MM-dd"),
                    GhostFilePath = ghost.FilePath
                };

                var detailsWindow = new Views.Popups.GhostDetailsWindow(submission, _currentTrackInfo);
                detailsWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error opening local ghost details: {FilePath}", ghost.FilePath);
            }
        }
    }
}