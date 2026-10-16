using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Mods.ViewModels;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.Mods.Views;

public partial class ModBrowserPage : UserControl, ILockedSidebarPage
{
    private readonly ModBrowserViewModel _model;
    private readonly INavigationService _navigation;
    private readonly IModalFactory _modals;
    private bool _fillingViewport;

    public ModBrowserPage(ModBrowserViewModel model, INavigationService navigation, IModalFactory modals)
    {
        _model = model;
        _navigation = navigation;
        _modals = modals;
        InitializeComponent();
        DataContext = model;
        Loaded += async (_, _) =>
        {
            await _model.SearchAsync();
            await FillViewportAsync();
        };
        ResultsScroll.ScrollChanged += async (_, _) => await FillViewportAsync();
        ResultsScroll.SizeChanged += (_, _) =>
        {
            var columns = ResultsScroll.Bounds.Width >= 520 ? 2 : 1;
            if (ResultsList.ItemsPanelRoot is Avalonia.Controls.Primitives.UniformGrid grid)
                grid.Columns = columns;
            LoadingGrid.Columns = columns;
        };
    }

    private async Task FillViewportAsync()
    {
        if (_fillingViewport)
            return;
        _fillingViewport = true;
        try
        {
            while (IsLoaded)
            {
                // Let each bounded batch render before deciding whether the viewport needs more results.
                await Dispatcher.UIThread.InvokeAsync(ResultsScroll.UpdateLayout, DispatcherPriority.Background);
                if (
                    !IsLoaded
                    || !_model.CanLoadMore
                    || ResultsScroll.Extent.Height - ResultsScroll.Offset.Y - ResultsScroll.Viewport.Height >= 200
                )
                    break;
                await _model.LoadMoreAsync();
            }
        }
        finally
        {
            _fillingViewport = false;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _model.Deactivate();
        base.OnDetachedFromVisualTree(e);
    }

    private void Back_Click(object? sender, RoutedEventArgs e) => _navigation.NavigateTo<ModsPage>();

    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        ResultsScroll.Offset = default;
        await _model.SearchAsync();
        await FillViewportAsync();
    }

    private void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        Search_Click(sender, e);
    }

    private void Filter_Click(object? sender, RoutedEventArgs e) => _model.PrepareFilters();

    private async void ApplyFilters_Click(object? sender, RoutedEventArgs e)
    {
        FilterButton.Flyout?.Hide();
        ResultsScroll.Offset = default;
        await _model.ApplyFiltersAsync();
        await FillViewportAsync();
    }

    private async void Retry_Click(object? sender, RoutedEventArgs e)
    {
        await _model.RetryAsync();
        await FillViewportAsync();
    }

    private async void Card_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ModBrowserItem item })
            await item.Preview.LoadAsync();
    }

    private async void Card_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ModBrowserItem item })
            return;
        var modal = _modals.Create<ModIndependentWindow>();
        modal.SetModalOwner(this);
        modal.Show();
        await modal.LoadModAsync(item.Mod.Id);
    }
}
