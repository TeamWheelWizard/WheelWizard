using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Models;
using WheelWizard.RrRooms;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Popups.Base;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups;

public partial class PlayerProfileWindow : PopupContent, INotifyPropertyChanged
{
    private const int ProfileCarouselPageCount = 3;

    [Inject]
    private IApiCaller<IRwfcApi> ApiCaller { get; set; } = null!;

    private PlayerProfileResponse? _profile;
    private RwfcPlayerRaceStatsResponse? _raceStats;
    private bool _isLoading = true;
    private string _errorMessage = string.Empty;
    private int _activeInfoSlideIndex;

    public PlayerProfileWindow(string friendCode)
        : base(true, true, false, "Player Profile")
    {
        InitializeComponent();
        DataContext = this;
        Window.WindowTitle = friendCode;
        LoadProfile(friendCode);
    }

    public PlayerProfileResponse? Profile
    {
        get => _profile;
        set
        {
            _profile = value;
            OnPropertyChanged(nameof(Profile));
            OnPropertyChanged(nameof(DisplayMii));
            OnPropertyChanged(nameof(LastSeenText));
            OnPropertyChanged(nameof(Vr24HoursText));
            OnPropertyChanged(nameof(VrWeekText));
            OnPropertyChanged(nameof(VrMonthText));
            OnPropertyChanged(nameof(RankText));
            OnPropertyChanged(nameof(CurrentVrText));
            OnPropertyChanged(nameof(HasProfile));
        }
    }

    public RwfcPlayerRaceStatsResponse? RaceStats
    {
        get => _raceStats;
        set
        {
            _raceStats = value;
            OnPropertyChanged(nameof(RaceStats));
            OnPropertyChanged(nameof(HasRaceStats));
            OnPropertyChanged(nameof(HasNoRaceStats));
            OnPropertyChanged(nameof(TotalRacesText));
            OnPropertyChanged(nameof(TrackedSinceText));
            OnPropertyChanged(nameof(FavoriteCharacterText));
            OnPropertyChanged(nameof(FavoriteCharacterMetaText));
            OnPropertyChanged(nameof(FavoriteVehicleText));
            OnPropertyChanged(nameof(FavoriteVehicleMetaText));
            OnPropertyChanged(nameof(FavoriteComboText));
            OnPropertyChanged(nameof(FavoriteComboMetaText));
            OnPropertyChanged(nameof(FavoriteTrackText));
            OnPropertyChanged(nameof(FavoriteTrackMetaText));
            OnPropertyChanged(nameof(BestCharacterText));
            OnPropertyChanged(nameof(BestCharacterMetaText));
            OnPropertyChanged(nameof(BestVehicleText));
            OnPropertyChanged(nameof(BestVehicleMetaText));
            OnPropertyChanged(nameof(RecentRaceText));
            OnPropertyChanged(nameof(RecentRaceMetaText));
            OnPropertyChanged(nameof(TimeInFirstText));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasProfile => Profile != null;
    public bool HasRaceStats => RaceStats != null;
    public bool HasNoRaceStats => RaceStats == null;

    public string RankText => Profile?.Rank > 0 ? $"Rank #{Profile.Rank:N0}" : "Unranked";
    public string CurrentVrText => Profile == null ? "--" : Profile.Vr.ToString("N0");
    public string Vr24HoursText => Profile?.VrStats == null ? "--" : FormatSignedValue(Profile.VrStats.Last24Hours);

    public string VrWeekText => Profile?.VrStats == null ? "--" : FormatSignedValue(Profile.VrStats.LastWeek);

    public string VrMonthText => Profile?.VrStats == null ? "--" : FormatSignedValue(Profile.VrStats.LastMonth);

    public int ActiveInfoSlideIndex
    {
        get => _activeInfoSlideIndex;
        set
        {
            var normalizedIndex = NormalizeCarouselIndex(value);
            if (_activeInfoSlideIndex == normalizedIndex)
                return;

            _activeInfoSlideIndex = normalizedIndex;
            OnPropertyChanged(nameof(ActiveInfoSlideIndex));
            UpdateCarouselIndicators();
        }
    }

    public string TotalRacesText => RaceStats == null ? "--" : RaceStats.TotalRaces.ToString("N0");
    public string TrackedSinceText => RaceStats == null ? "--" : RaceStats.TrackedSince.ToLocalTime().ToString("MMM d, yyyy");
    public string FavoriteCharacterText => RaceStats?.TopCharacters.FirstOrDefault()?.Name ?? "--";
    public string FavoriteCharacterMetaText => FormatRaceCount(RaceStats?.TopCharacters.FirstOrDefault()?.RaceCount);
    public string FavoriteVehicleText => RaceStats?.TopVehicles.FirstOrDefault()?.Name ?? "--";
    public string FavoriteVehicleMetaText => FormatRaceCount(RaceStats?.TopVehicles.FirstOrDefault()?.RaceCount);
    public string FavoriteComboText => RaceStats?.TopCombos.FirstOrDefault()?.Name ?? "--";
    public string FavoriteComboMetaText => FormatRaceCount(RaceStats?.TopCombos.FirstOrDefault()?.RaceCount);
    public string FavoriteTrackText => RaceStats?.TopTracks.FirstOrDefault()?.TrackName ?? "--";
    public string FavoriteTrackMetaText => FormatRaceCount(RaceStats?.TopTracks.FirstOrDefault()?.RaceCount);
    public string BestCharacterText => RaceStats?.TopCharactersByWinRate.FirstOrDefault()?.Name ?? "--";
    public string BestCharacterMetaText => FormatWinRate(RaceStats?.TopCharactersByWinRate.FirstOrDefault());
    public string BestVehicleText => RaceStats?.TopVehiclesByWinRate.FirstOrDefault()?.Name ?? "--";
    public string BestVehicleMetaText => FormatWinRate(RaceStats?.TopVehiclesByWinRate.FirstOrDefault());
    public string TimeInFirstText => RaceStats == null ? "--" : FormatDurationFromFrames(RaceStats.TotalFramesIn1st);

    public string RecentRaceText
    {
        get
        {
            var race = RaceStats?.RecentRaces.FirstOrDefault();
            if (race == null)
                return "--";

            var finish = race.FinishPos is > 0 ? $"{Ordinal(race.FinishPos.Value)} place" : "Finished";
            return $"{finish} on {race.TrackName}";
        }
    }

    public string RecentRaceMetaText
    {
        get
        {
            var race = RaceStats?.RecentRaces.FirstOrDefault();
            if (race == null)
                return "No recent races tracked";

            var setup =
                string.IsNullOrWhiteSpace(race.CharacterName) && string.IsNullOrWhiteSpace(race.VehicleName)
                    ? "Unknown setup"
                    : $"{race.CharacterName} / {race.VehicleName}";
            return $"{setup} - {FormatRelativeTime(race.Timestamp)}";
        }
    }

    public Mii? DisplayMii
    {
        get
        {
            if (Profile?.MiiData == null)
                return null;

            try
            {
                var result = MiiSerializer.Deserialize(Profile.MiiData);
                return result.IsSuccess ? result.Value : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public string LastSeenText
    {
        get
        {
            if (Profile == null)
                return string.Empty;

            var timeAgo = DateTime.UtcNow - Profile.LastSeen;
            if (timeAgo.TotalDays >= 1)
                return $"{(int)timeAgo.TotalDays} day(s) ago";
            if (timeAgo.TotalHours >= 1)
                return $"{(int)timeAgo.TotalHours} hour(s) ago";
            if (timeAgo.TotalMinutes >= 1)
                return $"{(int)timeAgo.TotalMinutes} minute(s) ago";
            return "Just now";
        }
    }

    private async void LoadProfile(string friendCode)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        RaceStats = null;

        var result = await ApiCaller.CallApiAsync(rwfcApi => rwfcApi.GetPlayerProfileAsync(friendCode));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (result.IsSuccess && result.Value != null)
            {
                Profile = result.Value;
                Window.WindowTitle = Profile.Name;
                IsLoading = false;
                _ = LoadRaceStatsAsync(Profile.Pid);
            }
            else
            {
                ErrorMessage = result.Error?.Message ?? "Failed to load profile";
                IsLoading = false;
            }
        });
    }

    private async Task LoadRaceStatsAsync(string pid)
    {
        if (string.IsNullOrWhiteSpace(pid))
            return;

        var result = await ApiCaller.CallApiAsync(rwfcApi => rwfcApi.GetPlayerRaceStatsAsync(pid, 1, 8));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (result.IsSuccess)
                RaceStats = result.Value;
        });
    }

    private void PrevCarouselPage_OnClick(object? sender, RoutedEventArgs e) => MoveCarouselPage(-1);

    private void NextCarouselPage_OnClick(object? sender, RoutedEventArgs e) => MoveCarouselPage(1);

    private void MoveCarouselPage(int offset)
    {
        ActiveInfoSlideIndex += offset;
    }

    private static int NormalizeCarouselIndex(int index)
    {
        var normalized = index % ProfileCarouselPageCount;
        return normalized < 0 ? normalized + ProfileCarouselPageCount : normalized;
    }

    private void UpdateCarouselIndicators()
    {
        SetDotActive(CarouselDot0, ActiveInfoSlideIndex == 0);
        SetDotActive(CarouselDot1, ActiveInfoSlideIndex == 1);
        SetDotActive(CarouselDot2, ActiveInfoSlideIndex == 2);
    }

    private static void SetDotActive(Border dot, bool active)
    {
        if (active && !dot.Classes.Contains("active"))
            dot.Classes.Add("active");
        else if (!active && dot.Classes.Contains("active"))
            dot.Classes.Remove("active");
    }

    private static string FormatSignedValue(int value)
    {
        if (value > 0)
            return $"+{value:N0}";

        return value.ToString("N0");
    }

    private static string FormatRaceCount(int? count)
    {
        if (count is null or <= 0)
            return "No tracked races";

        return count == 1 ? "1 tracked race" : $"{count:N0} tracked races";
    }

    private static string FormatWinRate(RwfcSetupWinRateEntry? entry)
    {
        if (entry == null)
            return "No win-rate sample";

        return $"{entry.WinRate:0.#}% wins across {entry.RaceCount:N0} races";
    }

    private static string FormatDurationFromFrames(long frames)
    {
        if (frames <= 0)
            return "--";

        var totalSeconds = frames / 60d;
        var time = TimeSpan.FromSeconds(totalSeconds);
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours}h {time.Minutes}m";

        return $"{time.Minutes}m {time.Seconds}s";
    }

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m ago";

        return "Just now";
    }

    private static string Ordinal(int number)
    {
        var suffix = number % 100 is 11 or 12 or 13
            ? "th"
            : (number % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };

        return $"{number}{suffix}";
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
