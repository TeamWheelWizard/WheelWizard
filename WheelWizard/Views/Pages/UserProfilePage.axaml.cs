using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models.Enums;
using WheelWizard.Models.GameData;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Other;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Pages;

public partial class UserProfilePage : UserControlBase, INotifyPropertyChanged
{
    private LicenseProfile? currentPlayer;
    private Mii? _currentMii;

    [Inject]
    private IGameLicenseSingletonService GameLicenseService { get; set; } = null!;

    public Mii? CurrentMii
    {
        get => _currentMii;
        set
        {
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
        var validUsers = GameLicenseService.HasAnyValidUsers;
        CurrentUserProfile.IsVisible = validUsers;
        CurrentUserCarousel.IsVisible = validUsers;
        NoProfilesInfo.IsVisible = !validUsers;

        var data = GameLicenseService.LicenseCollection;
        var userAmount = data.Users.Count;
        for (var i = 0; i < userAmount; i++)
        {
            var radioButton = RadioButtons.Children[i] as RadioButton;
            if (radioButton == null!)
                continue;

            var miiName = data.Users[i].Mii?.Name.ToString() ?? SettingValues.NoName;
            var noLicense = miiName == SettingValues.NoLicense;

            radioButton.IsEnabled = !noLicense;
            radioButton.Content = miiName switch
            {
                SettingValues.NoName => Online.NoName,
                SettingValues.NoLicense => Online.NoLicense,
                _ => miiName,
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
                IsEnabled = validRegions.Contains(region),
            };
            RegionDropdown.Items.Add(itemForRegionDropdown);

            if (currentRegion == region)
                RegionDropdown.SelectedItem = itemForRegionDropdown;
        }
    }

    private void TopBarRadio_OnClick(object? sender, RoutedEventArgs e)
    {
        var oldIndex = _currentUserIndex;

        if (sender is not RadioButton button || !int.TryParse((string?)button.Tag, out _currentUserIndex))
            return;
        if (oldIndex == _currentUserIndex)
            return;

        UpdatePage();
    }

    private void UpdatePage()
    {
        CurrentUserProfile.IsChecked = FocussedUser == _currentUserIndex;
        if (currentPlayer != null)
            currentPlayer.PropertyChanged -= OnMiiNameChanged;

        currentPlayer = GameLicenseService.GetUserData(_currentUserIndex);
        CurrentUserProfile.FriendCode = currentPlayer.FriendCode;
        CurrentUserProfile.UserName = currentPlayer.NameOfMii;
        CurrentUserProfile.IsOnline = currentPlayer.IsOnline;
        CurrentUserProfile.Vr = currentPlayer.Vr.ToString();
        CurrentUserProfile.Br = currentPlayer.Br.ToString();
        CurrentMii = currentPlayer.Mii;

        currentPlayer.PropertyChanged += OnMiiNameChanged;
        CurrentUserProfile.TotalRaces = currentPlayer.TotalRaceCount.ToString();
        CurrentUserProfile.TotalWon = currentPlayer.TotalWinCount.ToString();
        ResetMiiTopBar();
    }

    private void OnMiiNameChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(currentPlayer.NameOfMii))
            return;
        CurrentUserProfile.UserName = currentPlayer?.NameOfMii ?? "";
    }

    private void CheckBox_SetPrimaryUser(object sender, RoutedEventArgs e) => SetUserAsPrimary();

    private void SetUserAsPrimary()
    {
        if (FocussedUser == _currentUserIndex)
            return;

        SettingsManager.FOCUSSED_USER.Set(_currentUserIndex);

        CurrentUserProfile.IsChecked = true;
        // Even though it's true when this method is called, we still set it to true,
        // since Avalonia has some weird ass cashing, It might just be that that is because this method is actually deprecated

        //now we refresh the sidebar friend amount
        ViewUtils.GetLayout().UpdateFriendCount();
    }

    private void RegionDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RegionDropdown.SelectedItem is not ComboBoxItem { Tag: MarioKartWiiEnums.Regions region })
            return;

        SettingsManager.RR_REGION.Set(region);
        ResetMiiTopBar();
        var loadResult = GameLicenseService.LoadLicense();
        if (loadResult.IsFailure)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Failed to load game data")
                .SetInfoText(loadResult.Error.Message)
                .Show();
            return;
        }

        ViewMii(0); // Just in case you have current user set as 4. and you change to a region where there are only 3 users.
        SetUserAsPrimary();
        UpdatePage();
        ViewUtils.GetLayout().UpdateFriendCount();
    }

    // This is intentionally a separate validation method besides the true name validation. That name validation allows less than 3.
    // But we as team wheel wizard don't think it makes sense to have a mii name shorter than 3, and so from the UI we don't allow it
    private OperationResult ValidateMiiName(string? oldName, string newName)
    {
        if (newName.Length is > 10 or < 3)
            return Fail("Names must be between 3 and 10 characters long.");

        return Ok();
    }

    private async void ChangeMiiName(object? obj, EventArgs e)
    {
        var oldName = CurrentMii?.Name.ToString();
        var renamePopup = new TextInputWindow()
            .SetMainText($"Enter new name")
            .SetExtraText($"Changing name from: {oldName}")
            .SetAllowCustomChars(true)
            .SetValidation(ValidateMiiName)
            .SetInitialText(oldName ?? "")
            .SetPlaceholderText(oldName ?? "");

        var newName = await renamePopup.ShowDialog();
        if (oldName == newName || newName == null)
            return;
        var changeNameResult = GameLicenseService.ChangeMiiName(_currentUserIndex, newName);
        if (changeNameResult.IsFailure)
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Failed to change name")
                .SetInfoText(changeNameResult.Error.Message)
                .Show();
        else
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Message)
                .SetTitleText("Name changed")
                .SetInfoText($"Successfully changed name to {newName}")
                .Show();

        //reload game data, since multiple licenses can use the same mii
        GameLicenseService.LoadLicense();
        UpdatePage();
    }

    private void ViewRoom_OnClick(string friendCode)
    {
        foreach (var room in RRLiveRooms.Instance.CurrentRooms)
        {
            if (room.Players.All(player => player.Value.Fc != friendCode))
                continue;

            NavigationManager.NavigateTo<RoomDetailsPage>(room);
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
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
