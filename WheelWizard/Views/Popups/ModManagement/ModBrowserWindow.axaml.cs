using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Resources.Languages;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Popups.Base;
using WheelWizard.Views.Popups.Generic;
using VisualExtensions = Avalonia.VisualTree.VisualExtensions;

namespace WheelWizard.Views.Popups.ModManagement;

public record ModSearchResult(GameBananaModPreview Mod, string PreviewImageUrl);

public partial class ModBrowserWindow : PopupContent, INotifyPropertyChanged
{
    // Collection to hold the mods
    private ObservableCollection<ModSearchResult> Mods { get; } = [];
    private List<ModSearchResult> LoadedMods { get; } = [];

    [Inject]
    private IGameBananaSingletonService GameBananaService { get; set; } = null!;

    // Pagination variables
    private int _currentPage = 1;
    private bool _isLoading;
    private bool _hasMoreMods = true;
    private bool _isInitialLoad = true;

    private const double ScrollThreshold = 50; // Adjusted threshold for earlier loading

    private CancellationTokenSource? _loadCancellationToken;

    private string _currentSearchTerm = "";

    public ModBrowserWindow()
        : base(true, false, false, Common.PopupTitle_ModBrowser)
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
        if (!_isInitialLoad)
            return;

        LoadMods(_currentPage).ConfigureAwait(false);
        _isInitialLoad = false;

        // Attach to the ListBox's scroll event
        ModListView.AddHandler(ScrollViewer.ScrollChangedEvent, ModListView_ScrollChanged);
    }

    /// <summary>
    /// Loads mods for the specified page and search term.
    /// </summary>
    private async Task LoadMods(int page, string searchTerm = "", bool ensurePatchResults = true)
    {
        if (_isLoading || !_hasMoreMods)
            return;

        _isLoading = true;

        var effectiveSearchTerm = GetEffectiveSearchTerm(searchTerm);
        var result = await GameBananaService.GetModSearchResults(effectiveSearchTerm, page);

        if (result.IsFailure)
        {
            new MessageBoxWindow()
                .SetTitleText("Failed to load mods")
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetInfoText("Failed to retrieve mods. Make sure the request has at least 2 characters")
                .Show();
            _isLoading = false;
            return;
        }

        var metadata = result.Value.MetaData;
        var newMods = result.Value.Records.Where(mod => mod.ModelName == "Mod").ToList();

        foreach (var mod in newMods)
        {
            LoadedMods.Add(
                new(mod, mod.PreviewMedia != null ? mod.PreviewMedia.Images[0].BaseUrl + "/" + mod.PreviewMedia.Images[0].File : "")
            );
        }

        _hasMoreMods = !metadata.IsComplete;
        _currentPage = page;
        _isLoading = false;
        ApplyModListFilters();

        if (ShowPatchesOnly && ensurePatchResults)
            await EnsurePatchesOnlyResultsAsync();
    }

    /// <summary>
    /// Handles the ScrollChanged event to implement infinite scrolling.
    /// </summary>
    private async void ModListView_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isLoading || !_hasMoreMods)
            return;

        // Get the ScrollViewer from the ListBox's template
        var scrollViewer = VisualExtensions.FindDescendantOfType<ScrollViewer>(ModListView);
        if (scrollViewer == null)
            return;

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
        await ReloadSearchResults();
    }

    /// <summary>
    /// Handles the selection change in the ListView to display mod details.
    /// </summary>
    private async void ModListView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _loadCancellationToken?.Cancel(); //this cancels the previous load task if it's still running
        _loadCancellationToken = new();

        var modId = -1;
        if (ModListView.SelectedItem is ModSearchResult selectedMod)
        {
            if (selectedMod.Mod.Name == "LOADING")
                return;

            modId = selectedMod.Mod.Id;
        }
        try
        {
            await ModDetailViewer.LoadModDetailsAsync(modId, cancellationToken: _loadCancellationToken.Token);
        }
        catch (TaskCanceledException)
        {
            // Ignore
        }
    }

    protected override void BeforeClose()
    {
        // a bit dirty, but it's the easiest way to refresh the mod list in the ModsPage
        NavigationManager.NavigateTo<ModsPage>();
        base.BeforeClose();
    }

    private void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox)
            return;

        Search_Click(sender, e);
    }

    private bool ShowPatchesOnly => PatchesOnlyToggle.IsChecked == true;

    private string GetEffectiveSearchTerm(string searchTerm)
    {
        if (ShowPatchesOnly && string.IsNullOrWhiteSpace(searchTerm))
            return "Patches";

        return searchTerm;
    }

    private async Task ReloadSearchResults()
    {
        _currentPage = 1;
        _hasMoreMods = true;
        LoadedMods.Clear();
        await Dispatcher.UIThread.InvokeAsync(Mods.Clear);
        await LoadMods(_currentPage, _currentSearchTerm);
    }

    private void ApplyModListFilters()
    {
        var selectedModId = (ModListView.SelectedItem as ModSearchResult)?.Mod.Id;

        Mods.Clear();

        IEnumerable<ModSearchResult> visibleMods = LoadedMods;
        if (ShowPatchesOnly)
        {
            visibleMods = visibleMods.Where(mod => mod.Mod.UsesPatches);
        }
        else
        {
            visibleMods = visibleMods
                .Select((mod, index) => new { Mod = mod, Index = index })
                .OrderByDescending(entry => entry.Mod.Mod.UsesPatches)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Mod);
        }

        foreach (var mod in visibleMods)
            Mods.Add(mod);

        if (_hasMoreMods)
            Mods.Add(new(GameBananaService.GetLoadingPreview(), ""));

        ModListView.SelectedItem = selectedModId == null ? null : Mods.FirstOrDefault(mod => mod.Mod.Id == selectedModId);
    }

    private async Task EnsurePatchesOnlyResultsAsync()
    {
        while (ShowPatchesOnly && _hasMoreMods && !LoadedMods.Any(mod => mod.Mod.UsesPatches))
            await LoadMods(_currentPage + 1, _currentSearchTerm, false);
    }

    private async void PatchesOnlyToggle_Click(object? sender, RoutedEventArgs e)
    {
        await ReloadSearchResults();
    }

    #region Property Changed

    // Implement INotifyPropertyChanged
    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
