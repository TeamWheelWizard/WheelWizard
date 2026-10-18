using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Models;
using WheelWizard.RrRooms;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Couch;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WheelWizardData.Views.Dialogs;

public partial class PlayerProfileWindow : ModalContent, INotifyPropertyChanged
{
    private IApiCaller<IRwfcApi> ApiCaller { get; }

    private PlayerProfileResponse? _profile;
    private bool _isLoading = true;
    private string _errorMessage = string.Empty;

    public PlayerProfileWindow(VrHistoryGraph historyGraph, IApiCaller<IRwfcApi> apiCaller, string friendCode)
        : base("Player Profile")
    {
        ApiCaller = apiCaller;
        InitializeComponent();
        IsDismissible = true;
        ProfileMii.ArriveWith(["profile/window/hello", "profile/window/funny"]);
        HistoryHost.Content = historyGraph;
        historyGraph.Bind(VrHistoryGraph.FriendCodeProperty, new Binding("Profile.FriendCode") { Source = this });
        DataContext = this;
        ModalTitle = friendCode;
        LoadProfile(friendCode);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != CouchMode.IsEnabledProperty || PlayerProfileLayout is null)
            return;

        var couch = CouchMode.GetIsEnabled(this);
        PlayerProfileLayout.Width = couch ? 920 : 540;
        PlayerProfileLayout.MaxHeight = couch ? 480 : 760;
        PlayerProfileBody.ColumnDefinitions = couch ? new ColumnDefinitions("500,*") : new ColumnDefinitions("*");
        PlayerProfileBody.RowDefinitions = couch ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
        Grid.SetColumn(HistoryHost, couch ? 1 : 0);
        Grid.SetRow(HistoryHost, couch ? 0 : 1);
        HistoryHost.Margin = couch ? new Thickness(24, 0, 0, 0) : new Thickness(0);
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

    public string Vr24HoursText => Profile?.VrStats == null ? "--" : FormatSignedValue(Profile.VrStats.Last24Hours);

    public string VrWeekText => Profile?.VrStats == null ? "--" : FormatSignedValue(Profile.VrStats.LastWeek);

    public string VrMonthText => Profile?.VrStats == null ? "--" : FormatSignedValue(Profile.VrStats.LastMonth);

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

        var result = await ApiCaller.CallApiAsync(rwfcApi => rwfcApi.GetPlayerProfileAsync(friendCode));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (result.IsSuccess && result.Value != null)
            {
                Profile = result.Value;
                ModalTitle = Profile.Name;
                IsLoading = false;
            }
            else
            {
                ErrorMessage = result.Error?.Message ?? "Failed to load profile";
                IsLoading = false;
            }
        });
    }

    private void ViewOnRwfc_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Profile is { } profile)
            ViewUtils.OpenRwfcPlayer(profile.FriendCode);
    }

    private static string FormatSignedValue(int value)
    {
        if (value > 0)
            return $"+{value:N0}";

        return value.ToString("N0");
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
