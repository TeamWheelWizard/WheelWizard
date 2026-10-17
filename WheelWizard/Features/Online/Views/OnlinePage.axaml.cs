using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models.RRInfo;
using WheelWizard.Online.Domain;
using WheelWizard.Online.ViewModels;
using WheelWizard.RrRooms.Views;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WheelWizardData.Views.Dialogs;

namespace WheelWizard.Online.Views;

public partial class OnlinePage : UserControl
{
    private readonly OnlineViewModel _model;
    private readonly IPageFactory _pages;
    private readonly IModalFactory _modals;
    private bool _ready;

    public OnlinePage(OnlineViewModel model, IPageFactory pages, IModalFactory modals)
    {
        _model = model;
        _pages = pages;
        _modals = modals;
        InitializeComponent();
        DataContext = model;
        _ready = true;
        OverviewTab.IsChecked = true;
        Loaded += (_, _) => _model.Activate();
        Unloaded += (_, _) => _model.Deactivate();
    }

    private void ShowOverview()
    {
        SectionContent.Content = null;
        SectionContent.IsVisible = false;
        Overview.IsVisible = true;
        var podium = _pages.Create<LeaderboardPage>();
        podium.ShowAsSection(podiumOnly: true);
        Podium.Content = podium;
    }

    private void Section_OnCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not ToggleRadioButton { IsChecked: true })
            return;
        PlayerSearch.Text = "";
        Podium.Content = null;
        if (sender == OverviewTab)
        {
            ShowOverview();
            return;
        }
        Overview.IsVisible = false;
        SectionContent.IsVisible = true;
        if (sender == RoomsTab)
        {
            var rooms = _pages.Create<RoomsPage>();
            rooms.ShowAsSection();
            SectionContent.Content = rooms;
        }
        else
        {
            var leaderboard = _pages.Create<LeaderboardPage>();
            leaderboard.ShowAsSection();
            SectionContent.Content = leaderboard;
        }
    }

    private async void Search_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_ready)
            await _model.SearchAsync(PlayerSearch.Text);
    }

    private void ShowRooms_OnClick(object? sender, RoutedEventArgs e) => RoomsTab.IsChecked = true;

    private void ShowLeaderboard_OnClick(object? sender, RoutedEventArgs e) => LeaderboardTab.IsChecked = true;

    private void Room_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: RrRoom room } list)
        {
            list.SelectedItem = null;
            _modals.Create<RoomDetailsWindow>(room).Show();
        }
    }

    private void Player_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is PlayerListItem { DataContext: OnlinePlayer player })
        {
            _modals.Create<PlayerProfileWindow>(player.FriendCode).Show();
        }
    }
}
