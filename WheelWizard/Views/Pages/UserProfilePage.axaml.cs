using Avalonia.Controls;
using Avalonia.Interactivity;
using System.ComponentModel;
using WheelWizard.Models.Enums;
using WheelWizard.Models.GameData;
using WheelWizard.Models.MiiImages;
using WheelWizard.Services.Other;
using WheelWizard.Services.Settings;
using WheelWizard.Services.WiiManagement.SaveData;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.LiveData;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Pages;

public partial class UserProfilePage : UserControl, INotifyPropertyChanged
{
    private GameDataUser? currentPlayer;
    private Mii? _currentMii;
    public Mii? CurrentMii 
    { 
        get => _currentMii;
        set { 
            _currentMii = value;
            OnPropertyChanged(nameof(CurrentMii));
        }
    }

    private int _currentUserIndex;
    private static int FocussedUser => (int)SettingsManager.FOCUSSED_USER.Get();
    
    public UserProfilePage()
    {
        InitializeComponent();
        ResetMiiTopBar();
        ViewMii(FocussedUser);
        PopulateRegions();
        UpdatePage();
        DataContext = this;
        // Make sure this action gets subscribed AFTER the PopulateRegions method
        RegionDropdown.SelectionChanged += RegionDropdown_SelectionChanged;
    }
    
    private void ResetMiiTopBar()
    {
        var validUsers = GameDataLoader.Instance.HasAnyValidUsers;
        CurrentUserProfile.IsVisible = validUsers;
        CurrentUserCarousel.IsVisible = validUsers;
        NoProfilesInfo.IsVisible = !validUsers;
        
        var data = GameDataLoader.Instance.GetGameData;
        var userAmount = data.Users.Count;
        for (var i = 0; i < userAmount; i++)
        {
            var radioButton = RadioButtons.Children[i] as RadioButton;
            if (radioButton == null!) 
                continue;
            
            var miiName = data.Users[i].MiiData?.Mii?.Name ?? SettingValues.NoName;
            var noLicense = miiName == SettingValues.NoLicense;
            
            radioButton.IsEnabled = !noLicense;
            radioButton.Content = miiName switch
            {
                SettingValues.NoName    => Online.NoName,
                SettingValues.NoLicense => Online.NoLicense,
                _                       => miiName
            };
        }
    }

    private void ViewMii(int? mii = null)
    {
        _currentUserIndex = mii ?? _currentUserIndex;
        if (RadioButtons.Children[_currentUserIndex] is RadioButton radioButton)
            radioButton.IsChecked = true;
    }

    private void PopulateRegions()
    {
        var validRegions = RRRegionManager.GetValidRegions();
        var currentRegion = (MarioKartWiiEnums.Regions)SettingsManager.RR_REGION.Get();
        foreach (var region in Enum.GetValues<MarioKartWiiEnums.Regions>())
        {
            if (region == MarioKartWiiEnums.Regions.None)
                continue;
            
            var itemForRegionDropdown = new ComboBoxItem
            {
                Content = region.ToString(),
                Tag = region,
                IsEnabled = validRegions.Contains(region)
            };
            RegionDropdown.Items.Add(itemForRegionDropdown);

            if (currentRegion == region)
                RegionDropdown.SelectedItem = itemForRegionDropdown;
        }
    }
    
    private void TopBarRadio_OnClick(object? sender, RoutedEventArgs e)
    {
        var oldIndex = _currentUserIndex;
        
        if (sender is not RadioButton button || !int.TryParse((string?)button.Tag, out _currentUserIndex )) 
            return;
        if (oldIndex == _currentUserIndex) 
            return;
        
        UpdatePage();
    }
    
    private void UpdatePage()
    {
        CurrentUserProfile.IsChecked = FocussedUser == _currentUserIndex;
        if(currentPlayer != null) currentPlayer.PropertyChanged -= OnMiiNameChanged;
        
        currentPlayer = GameDataLoader.Instance.GetUserData(_currentUserIndex);
        CurrentUserProfile.FriendCode = currentPlayer.FriendCode;
        CurrentUserProfile.UserName = currentPlayer.MiiName;
        CurrentUserProfile.IsOnline = currentPlayer.IsOnline;
        CurrentUserProfile.Vr = currentPlayer.Vr.ToString();
        CurrentUserProfile.Br = currentPlayer.Br.ToString();
        CurrentMii = currentPlayer.MiiData?.Mii;

        currentPlayer.PropertyChanged += OnMiiNameChanged;
        CurrentUserProfile.TotalRaces = currentPlayer.TotalRaceCount.ToString();
        CurrentUserProfile.TotalWon =currentPlayer.TotalWinCount.ToString();
    }

    private void OnMiiNameChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(currentPlayer.MiiName)) return;
        CurrentUserProfile.UserName = currentPlayer.MiiName;
    }
    
    private void CheckBox_SetPrimaryUser(object sender, RoutedEventArgs e) => SetUserAsPrimary();
    private void SetUserAsPrimary()
    {
        if(FocussedUser == _currentUserIndex)
            return;
       
        SettingsManager.FOCUSSED_USER.Set(_currentUserIndex);
        
        CurrentUserProfile.IsChecked = true; 
        // Even though it's true when this method is called, we still set it to true,
        // since Avalonia has some weird ass cashing, It might just be that that is because this method is actually deprecated
    }
    
    private void RegionDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RegionDropdown.SelectedItem is not ComboBoxItem { Tag: MarioKartWiiEnums.Regions region }) 
            return;
        
        SettingsManager.RR_REGION.Set(region);
        GameDataLoader.Instance.LoadGameData();
        ResetMiiTopBar();
        
        ViewMii(0); // Just in case you have current user set as 4. and you change to a region where there are only 3 users.
        SetUserAsPrimary();
        
        UpdatePage();
    }
    
    private void ChangeMiiName(object? obj, EventArgs e)
    {
        GameDataLoader.Instance.PromptLicenseNameChange(_currentUserIndex);
    }
    
    private void ViewRoom_OnClick(string friendCode)
    {
        foreach (var room in RRLiveRooms.Instance.CurrentRooms)
        {
            if (room.Players.All(player => player.Value.Fc != friendCode))
                continue;
    
            ViewUtils.NavigateToPage(new RoomDetailsPage(room));
            return;
        }
       
        new MessageBoxWindow()
            .SetTitleText("Couldn't find the room")
            .SetInfoText("Whoops, could not find the room that this player is supposedly playing in")
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .Show();
    }
    
    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}
