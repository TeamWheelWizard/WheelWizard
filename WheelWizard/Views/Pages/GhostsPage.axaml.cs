using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models;
using WheelWizard.Services;
using WheelWizard.Views;
using WheelWizard.Views.Patterns;
using WheelWizard.Shared.DependencyInjection;
using Serilog;

namespace WheelWizard.Views.Pages;

public partial class GhostsPage : UserControlBase, INotifyPropertyChanged
{
    [Inject] private GhostTrackService _ghostTrackService { get; set; } = null!;
    private ObservableCollection<ApiTrack> _tracks = new();
    private ObservableCollection<ApiTrack> _filteredTracks = new();
    private string _searchText = string.Empty;
    private bool _isLoading = false;

    public ObservableCollection<ApiTrack> FilteredTracks
    {
        get => _filteredTracks;
        set
        {
            _filteredTracks = value;
            OnPropertyChanged(nameof(FilteredTracks));
            UpdateTrackCount();
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

    public GhostsPage()
    {
        InitializeComponent();
        DataContext = this;
        
        TrackListBox.SelectionChanged += TrackListBox_SelectionChanged;
        _ = LoadTracksAsync();
    }

    private void UpdateTrackCount()
    {
        if (TrackCount != null)
        {
            TrackCount.Text = _filteredTracks.Count.ToString();
        }
    }

    private void TrackListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TrackListBox.SelectedItem != null)
        {
            TrackListBox.SelectedItem = null;
        }
    }

    private async Task LoadTracksAsync()
    {
        try
        {
            IsLoading = true;
            Log.Information("Loading tracks from API");
            
            var allTracks = await _ghostTrackService.GetAllTracksAsync();
            var trackFilter = GetSelectedTrackFilter();
            var locationFilter = GetSelectedLocationFilter();
            
            var filteredTracks = _ghostTrackService.GetFilteredTracks(trackFilter, locationFilter, _searchText);
            
            _tracks = new ObservableCollection<ApiTrack>(allTracks);
            FilteredTracks = new ObservableCollection<ApiTrack>(filteredTracks);
            
            Log.Information("Loaded {Count} tracks", allTracks.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load tracks");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySearchFilter()
    {
        if (_tracks == null)
        {
            FilteredTracks = new ObservableCollection<ApiTrack>();
            return;
        }

        var trackFilter = GetSelectedTrackFilter();
        var locationFilter = GetSelectedLocationFilter();
        var filteredTracks = _ghostTrackService.GetFilteredTracks(trackFilter, locationFilter, _searchText);
        
        FilteredTracks = new ObservableCollection<ApiTrack>(filteredTracks);
    }

    private GhostTrackType GetSelectedTrackFilter()
    {
        if (RetroTracksRadio?.IsChecked == true) return GhostTrackType.Retro;
        if (CustomTracksRadio?.IsChecked == true) return GhostTrackType.Custom;
        return GhostTrackType.All;
    }

    private GhostLocation GetSelectedLocationFilter()
    {
        return GhostLocation.All;
    }

    private void FilterChanged(object? sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ApplySearchFilter();
    }

    private void SearchTextBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _searchText = textBox.Text ?? string.Empty;
            ApplySearchFilter();
        }
    }

    private void ViewTimesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ApiTrack track)
        {
            var ghostTrack = new GhostTrack
            {
                TrackId = track.TrackId, 
                Name = track.Name,
                Console = track.Console,
                TrackType = track.TrackType,
                HexValue = track.HexValue 
            };
            NavigationManager.NavigateTo<GhostTimesPage>(ghostTrack);
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}