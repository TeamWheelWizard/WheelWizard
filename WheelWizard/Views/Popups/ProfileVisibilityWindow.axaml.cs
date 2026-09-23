using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Interactivity;
using WheelWizard.CloudSync.ProfileLibrary;
using WheelWizard.Views.Popups.Base;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups;

public sealed class ProfileVisibilityChoice : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _canSelect = true;

    public ProfileVisibilityChoice(ProfileLibraryEntry entry, bool isSelected)
    {
        Entry = entry;
        _isSelected = isSelected;
    }

    public ProfileLibraryEntry Entry { get; }
    public string Name => Entry.Name;
    public string FriendCode => string.IsNullOrWhiteSpace(Entry.FriendCode) ? "Offline license" : Entry.FriendCode;
    public string LastUpdated => Entry.LastUpdatedUtc is { } time ? $"Last updated: {time.ToLocalTime():g}" : "Last updated: unavailable";
    public Mii? Mii => Entry.Mii;
    public bool IsCloudOnly => Entry.StorageState == ProfileStorageState.CloudOnly;
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

    public bool CanSelect
    {
        get => _canSelect;
        internal set
        {
            if (_canSelect == value)
                return;
            _canSelect = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelect)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ProfileVisibilityWindow : PopupContent
{
    private readonly TaskCompletionSource<IReadOnlyList<string>?> _result = new();
    private List<string> _selectedKeys = [];

    public ObservableCollection<ProfileVisibilityChoice> Choices { get; } = [];

    public ProfileVisibilityWindow()
        : base(true, false, true, "Visible profiles")
    {
        InitializeComponent();
        DataContext = this;
        Choices.CollectionChanged += (_, _) => UpdateSelectionAvailability();
    }

    public ProfileVisibilityWindow SetProfiles(IEnumerable<ProfileLibraryEntry> profiles, IEnumerable<string> selectedKeys)
    {
        _selectedKeys = selectedKeys.Distinct(StringComparer.Ordinal).ToList();
        var selected = _selectedKeys.ToHashSet(StringComparer.Ordinal);
        Choices.Clear();
        foreach (var profile in profiles)
        {
            var choice = new ProfileVisibilityChoice(profile, selected.Contains(profile.Key));
            choice.PropertyChanged += Choice_OnPropertyChanged;
            Choices.Add(choice);
        }
        UpdateSelectionAvailability();
        return this;
    }

    public Task<IReadOnlyList<string>?> AwaitAnswer()
    {
        Show();
        return _result.Task;
    }

    private void Apply_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectedSet = Choices.Where(choice => choice.IsSelected).Select(choice => choice.Entry.Key).ToHashSet(StringComparer.Ordinal);
        var selected = _selectedKeys
            .Where(selectedSet.Contains)
            .Concat(
                Choices
                    .Where(choice => choice.IsSelected && !_selectedKeys.Contains(choice.Entry.Key, StringComparer.Ordinal))
                    .Select(choice => choice.Entry.Key)
            )
            .ToList();
        if (selected.Count > 4)
        {
            Validation.Text = "Select at most four profiles.";
            return;
        }
        _result.TrySetResult(selected);
        Close();
    }

    private void Choice_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProfileVisibilityChoice.IsSelected))
            return;
        if (Choices.Count(choice => choice.IsSelected) > 4 && sender is ProfileVisibilityChoice choice)
        {
            choice.IsSelected = false;
            Validation.Text = "Only four profiles can be visible in WiiCompiled at once.";
        }
        else
            Validation.Text = string.Empty;
        UpdateSelectionAvailability();
    }

    private void UpdateSelectionAvailability()
    {
        var atLimit = Choices.Count(choice => choice.IsSelected) >= 4;
        foreach (var choice in Choices)
            choice.CanSelect = choice.IsSelected || !atLimit;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        _result.TrySetResult(null);
        Close();
    }

    protected override void BeforeClose() => _result.TrySetResult(null);
}
