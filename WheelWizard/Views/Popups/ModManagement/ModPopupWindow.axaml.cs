using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WheelWizard.Models.GameBanana;
using WheelWizard.Services.GameBanana;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Popups.Generic;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace WheelWizard.Views.Popups.ModManagement;

public partial class ModPopupWindow : PopupContent, INotifyPropertyChanged
{
    // Collection to hold the mods
    private ObservableCollection<GameBananaModDetails> Mods { get; } = new ObservableCollection<GameBananaModDetails>();

    // Pagination variables
    private int _currentPage = 1;
    private bool _isLoading = false;
    private bool _hasMoreMods = true;
    private bool _isInitialLoad = true;

    private const int ModsPerPage = 15;
    private const double ScrollThreshold = 50; // Adjusted threshold for earlier loading
    
    private CancellationTokenSource? _loadCancellationToken;


    private string _currentSearchTerm = "";

    public ModPopupWindow() : base(true, false, false, "Mod Browser", new Vector(800, 800))
    {
        InitializeComponent();
        DataContext = this;
        ModListView.ItemsSource = Mods;
        Loaded += ModPopupWindow_Loaded;
    }

    /// <summary>
    /// Finds the ScrollViewer within the ListView.
    /// </summary>
    private void ModPopupWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialLoad) return;
        LoadMods(_currentPage).ConfigureAwait(false);
        _isInitialLoad = false;
        
        // Attach to the ListBox's scroll event
        ModListView.AddHandler(ScrollViewer.ScrollChangedEvent, ModListView_ScrollChanged);
    }

    /// <summary>
    /// Loads mods for the specified page and search term.
    /// </summary>
    private async Task LoadMods(int page, string searchTerm = "")
    {
        if (_isLoading) return;
        if (!_hasMoreMods) return;
        _isLoading = true;
        try
        {
            var result = await GamebananaSearchHandler.SearchModsAsync(searchTerm, page, ModsPerPage);
            Mods.Where(mod => mod._sName == "LOADING").ToList().ForEach(mod => Mods.Remove(mod));
            
            if (result is { Succeeded: true, Content: not null })
            {
                var metadata = result.Content._aMetadata;
                var newMods = result.Content._aRecords
                    .Where(mod => mod._sModelName == "Mod")
                    .ToList();
                if (newMods.Count > 0)
                {
                    foreach (var mod in newMods)
                    {
                        Mods.Add(mod);
                    }
                    _hasMoreMods = !metadata._bIsComplete;
                    _currentPage = page;
                }
                else
                {
                    // If no new mods were fetched, rely on metadata
                    _hasMoreMods = !result.Content._aMetadata._bIsComplete;
                }

                if (_hasMoreMods)
                    Mods.Add(GameBananaModDetails.LoadingMod());
            }
            else
            {
                new MessageBoxWindow()
                    .SetTitleText("Failed to load mods")
                    .SetMessageType(MessageBoxWindow.MessageType.Warning)
                    .SetInfoText("Failed to find new mods. Make sure the request has at least 2 characters")
                    .Show();
            }
        }
        catch (Exception e)
        {
            new MessageBoxWindow()
                .SetTitleText("Failed to load mods")
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetInfoText($"Something went wrong while trying to load mods. Error: {e}")
                .Show();
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Handles the ScrollChanged event to implement infinite scrolling.
    /// </summary>
    private async void ModListView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isLoading) return;
        if (!_hasMoreMods) return;

        // Get the ScrollViewer from the ListBox's template
        var scrollViewer = VisualExtensions.FindDescendantOfType<ScrollViewer>(ModListView);
        if (scrollViewer == null) return;

        // Check if we're near the bottom of the scrollable content
        var verticalOffset = scrollViewer.Offset.Y;
        var extentHeight = scrollViewer.Extent.Height;
        var viewportHeight = scrollViewer.Viewport.Height;

        // Calculate remaining scroll distance (adjusting for a potential rounding error)
        var remainingScroll = extentHeight - verticalOffset - viewportHeight;

        // Load more when we're within the threshold of the bottom
        if (remainingScroll <= ScrollThreshold)
            await LoadMods(_currentPage + 1, _currentSearchTerm);
    }

    /// <summary>
    /// Handles the Search button click event.
    /// </summary>
    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        _currentSearchTerm = SearchTextBox.Text?.Trim() ?? "";
        _currentPage = 1;
        _hasMoreMods = true;

        Dispatcher.UIThread.InvokeAsync(Mods.Clear);
        await LoadMods(_currentPage, _currentSearchTerm);
    }

    /// <summary>
    /// Handles the selection change in the ListView to display mod details.
    /// </summary>
    private async void ModListView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _loadCancellationToken?.Cancel(); //this cancels the previous load task if it's still running
        _loadCancellationToken = new CancellationTokenSource();

        var modId = -1;
        if (ModListView.SelectedItem is GameBananaModDetails selectedMod)
            modId = selectedMod._idRow;
        try
        {
            await ModDetailViewer.LoadModDetailsAsync(modId, cancellationToken: _loadCancellationToken.Token);
        }
        catch (TaskCanceledException)
        {
            // Ignore
        }
    }
    
    // Implement INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected override void BeforeClose()
    {
        // a bit dirty, but it's the easiest way to refresh the mod list in the ModsPage
        ViewUtils.NavigateToPage(new ModsPage());
        base.BeforeClose();
    }

    private void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox) return;
        Search_Click(sender, e);
    }
}
