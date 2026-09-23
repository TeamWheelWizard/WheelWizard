using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Interactivity;
using WheelWizard.CloudSync;
using WheelWizard.CloudSync.ProfileLibrary;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Pages;

public sealed class CloudSyncProfileChoice(ProfileLibraryEntry entry, bool selected) : INotifyPropertyChanged
{
    private bool _isSelected = selected;

    public ProfileLibraryEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string FriendCode => string.IsNullOrWhiteSpace(Entry.FriendCode) ? "Offline license" : Entry.FriendCode;
    public string LastUpdated => Entry.LastUpdatedUtc is { } time ? $"Last updated: {time.ToLocalTime():g}" : "Last updated: unavailable";
    public Mii? Mii => Entry.Mii;
    public bool CanSync => Entry.Source is ProfileLibrarySource.Local or ProfileLibrarySource.Vault;
    public bool IsLocalOnly => Entry.StorageState == ProfileStorageState.LocalOnly;
    public bool IsCloudAndLocal => Entry.StorageState == ProfileStorageState.CloudAndLocal;
    public string StorageStatus =>
        Entry.StorageState switch
        {
            ProfileStorageState.CloudOnly => "Available in cloud only",
            ProfileStorageState.LocalOnly => "Available locally only",
            ProfileStorageState.CloudAndLocal => "Available in cloud and on this device",
            _ => string.Empty,
        };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class CloudPage : UserControlBase
{
    private bool _syncInProgress;

    [Inject]
    private ICloudProfileLibraryService ProfileLibraryService { get; set; } = null!;

    [Inject]
    private ICloudSyncService CloudSync { get; set; } = null!;

    public ObservableCollection<CloudSyncProfileChoice> Profiles { get; } = [];

    public CloudPage()
    {
        InitializeComponent();
        DataContext = this;
        _ = LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        Status.Text = "Loading local and cloud profiles...";
        var profiles = await ProfileLibraryService.GetAllAsync();
        var selected = ProfileLibraryService.GetSyncSelected(profiles).Select(profile => profile.Key).ToHashSet(StringComparer.Ordinal);
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(new CloudSyncProfileChoice(profile, selected.Contains(profile.Key)));

        Status.Text =
            Profiles.Count == 0
                ? "No Mario Kart licenses were found on this device."
                : "Select the local profiles you want to sync, then save your selection.";
    }

    private void SaveSelection_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveSelection();
        Status.Text = "Cloud profile selection saved.";
    }

    private async void ChooseVisibleProfiles_OnClick(object? sender, RoutedEventArgs e)
    {
        var profiles = (await ProfileLibraryService.GetAllAsync()).ToList();
        var visible = ProfileLibraryService.GetVisible(profiles).Select(profile => profile.Key);
        var selected = await new ProfileVisibilityWindow().SetProfiles(profiles, visible).AwaitAnswer();
        if (selected is null)
            return;

        ProfileLibraryService.SaveVisible(selected);
        await LoadProfilesAsync();
        Status.Text = "Visible profiles updated.";
    }

    private async void SyncSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_syncInProgress)
            return;
        _syncInProgress = true;
        SaveSelection();
        Status.Text = "Synchronizing selected profiles...";
        SyncButton.Text = "Syncing profiles...";
        SyncButton.IsLoading = true;
        SyncButton.Variant = WheelWizard.Views.Components.Button.ButtonsVariantType.Primary;
        try
        {
            var result = await CloudSync.SyncNowAsync();
            await LoadProfilesAsync();
            Status.Text = result.Message;
        }
        finally
        {
            _syncInProgress = false;
            SyncButton.IsLoading = false;
            SyncButton.Text = "Sync selected profiles";
            SyncButton.Variant = WheelWizard.Views.Components.Button.ButtonsVariantType.Default;
        }
    }

    private void SaveSelection()
    {
        var selected = Profiles.Where(profile => profile.CanSync && profile.IsSelected).Select(profile => profile.Entry.Key);
        ProfileLibraryService.SaveSyncSelected(selected);
    }

    private void CloudSettings_OnClick(object? sender, RoutedEventArgs e) =>
        NavigationManager.NavigateTo<SettingsPage>(new Settings.CloudSaveSettings());
}
