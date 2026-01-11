using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using WheelWizard.Models;
using WheelWizard.Resources.Languages;
using WheelWizard.RrRooms;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Popups.Base;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups;

public partial class PlayerProfileWindow : PopupContent, INotifyPropertyChanged
{
    [Inject]
    private IApiCaller<IRwfcApi> ApiCaller { get; set; } = null!;

    private PlayerProfileResponse? _profile;
    private bool _isLoading = true;
    private string _errorMessage = string.Empty;

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
                Window.WindowTitle = Profile.Name;
                IsLoading = false;
            }
            else
            {
                ErrorMessage = result.Error.Message ?? "Failed to load profile";
                IsLoading = false;
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
