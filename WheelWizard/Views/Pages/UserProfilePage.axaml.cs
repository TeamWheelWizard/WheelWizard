using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Other;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Components;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Pages;

public partial class UserProfilePage : UserControlBase, INotifyPropertyChanged
{
    private LicenseProfile? currentPlayer;
    private Mii? _currentMii;
    private bool _isOnline;

    [Inject]
    private IGameLicenseSingletonService GameLicenseService { get; set; } = null!;

    [Inject]
    private IWhWzDataSingletonService BadgeService { get; set; } = null!;

    [Inject]
    private IMiiDbService MiiDbService { get; set; } = null!;

    public Mii? CurrentMii
    {
        get => _currentMii;
        set
        {
            _currentMii = value;
            OnPropertyChanged(nameof(CurrentMii));
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            _isOnline = value;
            OnPropertyChanged(nameof(IsOnline));
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

    private void PopulateRegions()
    {
        var validRegions = RRRegionManager.GetValidRegions();
        var currentRegion = (MarioKartWiiEnums.Regions)SettingsManager.RR_REGION.Get();
        foreach (var region in Enum.GetValues<MarioKartWiiEnums.Regions>())
        {
            if (region == MarioKartWiiEnums.Regions.None)
                continue;

            var name = region switch
            {
                MarioKartWiiEnums.Regions.Europe => Common.Region_Europe,
                MarioKartWiiEnums.Regions.America => Common.Region_America,
                MarioKartWiiEnums.Regions.Korea => Common.Region_SouthKorea,
                MarioKartWiiEnums.Regions.Japan => Common.Region_Japan,
                _ => Common.State_Unknown,
            };
            var itemForRegionDropdown = new ComboBoxItem
            {
                Content = name,
                Tag = region,
                IsEnabled = validRegions.Contains(region),
            };
            RegionDropdown.Items.Add(itemForRegionDropdown);

            if (currentRegion == region)
                RegionDropdown.SelectedItem = itemForRegionDropdown;
        }
    }

    #region Update page

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
                SettingValues.NoName => Common.State_NoName,
                SettingValues.NoLicense => Common.State_NoLicense,
                _ => miiName,
            };
        }
    }

    private void UpdatePage()
    {
        PrimaryCheckBox.IsChecked = FocussedUser == _currentUserIndex;
        CurrentUserProfile.Classes.Clear();

        currentPlayer = GameLicenseService.GetUserData(_currentUserIndex);
        ProfileAttribFriendCode.Text = currentPlayer.FriendCode;
        ProfileAttribFriendCode.IsVisible = !string.IsNullOrEmpty(currentPlayer.FriendCode);
        ProfileAttribUserName.Text = currentPlayer.NameOfMii;
        ProfileAttribVr.Text = currentPlayer.Vr.ToString();
        ProfileAttribBr.Text = currentPlayer.Br.ToString();
        CurrentMii = currentPlayer.Mii;
        IsOnline = currentPlayer.IsOnline;
        if (IsOnline)
            CurrentUserProfile.Classes.Add("Online");

        ProfileAttribTotalRaces.Text = currentPlayer.Statistics.RaceTotals.OnlineRacesCount.ToString();
        ProfileAttribTotalWins.Text = currentPlayer.Statistics.RaceTotals.WinsVsLosses.OnlineVs.Wins.ToString();

        BadgeContainer.Children.Clear();
        var badges = BadgeService.GetBadges(currentPlayer.FriendCode).Select(variant => new Badge { Variant = variant });
        foreach (var badge in badges)
        {
            badge.Height = 30;
            badge.Width = 30;
            BadgeContainer.Children.Add(badge);
        }

        ResetMiiTopBar();
    }

    #endregion

    private void ViewMii(int? mii = null)
    {
        _currentUserIndex = mii ?? _currentUserIndex;
        if (RadioButtons.Children[_currentUserIndex] is RadioButton radioButton)
            radioButton.IsChecked = true;
    }

    private void SetUserAsPrimary()
    {
        if (FocussedUser == _currentUserIndex)
            return;

        SettingsManager.FOCUSSED_USER.Set(_currentUserIndex);

        PrimaryCheckBox.IsChecked = true;
        // Even though it's true when this method is called, we still set it to true,
        // since Avalonia has some weird ass cashing, It might just be that that is because this method is actually deprecated

        //now we refresh the sidebar friend amount
        ViewUtils.GetLayout().UpdateFriendCount();
        ViewUtils.ShowSnackbar(Phrases.SnackbarSuccess_ProfileSetPrimary);
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

    private void TopBarRadio_OnClick(object? sender, RoutedEventArgs e)
    {
        var oldIndex = _currentUserIndex;

        if (sender is not RadioButton button || !int.TryParse((string?)button.Tag, out _currentUserIndex))
            return;
        if (oldIndex == _currentUserIndex)
            return;

        UpdatePage();
    }

    private void CheckBox_SetPrimaryUser(object sender, RoutedEventArgs e) => SetUserAsPrimary();

    private async void OpenMiiSelector_Click(object? sender, RoutedEventArgs e)
    {
        var availableMiis = MiiDbService.GetAllMiis();
        if (!availableMiis.Any())
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_NoMiisFound);
            return;
        }

        var selectedMii = await new MiiSelectorWindow().SetMiiOptions(availableMiis, CurrentMii).AwaitAnswer();

        if (selectedMii == null)
            return;

        var result = GameLicenseService.ChangeMii(_currentUserIndex, selectedMii);

        if (result.IsFailure)
        {
            new MessageBoxWindow()
                .SetTitleText(Phrases.MessageError_FailedChangeMii_Title)
                .SetInfoText(result.Error!.Message)
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .Show();
            return;
        }

        CurrentMii = selectedMii;
        GameLicenseService.LoadLicense();
        UpdatePage();
        ViewUtils.ShowSnackbar(Phrases.MessageSuccess_MiiChanged);
    }

    private void ViewRoom_OnClick(object? sender, RoutedEventArgs e)
    {
        foreach (var room in RRLiveRooms.Instance.CurrentRooms)
        {
            if (room.Players.All(player => player.Value.Fc != currentPlayer?.FriendCode))
                continue;

            NavigationManager.NavigateTo<RoomDetailsPage>(room);
            return;
        }

        MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CouldNotFindRoom);
    }

    private void CopyFriendCode_OnClick(object? sender, EventArgs e)
    {
        if (currentPlayer?.FriendCode == null)
            return;

        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(currentPlayer.FriendCode);
        ViewUtils.ShowSnackbar(Phrases.SnackbarSuccess_CopiedFC);
    }

    // This is intentionally a separate validation method besides the true name validation. That name validation allows less than 3.
    // But we as team wheel wizard don't think it makes sense to have a mii name shorter than 3, and so from the UI we don't allow it
    private OperationResult ValidateMiiName(string? oldName, string newName)
    {
        newName = newName?.Trim();
        if (newName.Length is > 10 or < 3)
            return Fail(Phrases.HelperNote_NameMustBetween);

        return Ok();
    }

    private async void RenameMii_OnClick(object? sender, EventArgs e)
    {
        var oldName = CurrentMii?.Name.ToString();
        var renamePopup = new TextInputWindow()
            .SetMainText(Phrases.Question_EnterNewName_Title)
            .SetExtraText(Humanizer.ReplaceDynamic(Phrases.Question_EnterNewName_Extra, oldName))
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
                .SetTitleText(Phrases.MessageError_FailedChangeName_Title)
                .SetInfoText(changeNameResult.Error.Message)
                .Show();
        else
            ViewUtils.ShowSnackbar(Humanizer.ReplaceDynamic(Phrases.SnackbarSuccess_NameChange, newName) ?? "Name changed successfully");

        //reload game data, since multiple licenses can use the same mii
        GameLicenseService.LoadLicense();
        UpdatePage();
    }

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
