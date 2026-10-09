using System.IO.Abstractions;
using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.MiiImages.Views;
using WheelWizard.Mods;
using WheelWizard.Mods.Views;
using WheelWizard.RrRooms.Views;
using WheelWizard.Settings;
using WheelWizard.Settings.Views;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Components;
using WheelWizard.Views.DesignTime;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Controls;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Startup;
using WheelWizard.Views.Shell.Views;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement.MiiManagement.Views;
using Button = Avalonia.Controls.Button;

namespace WheelWizard.UI.Test;

public class ApplicationCompositionTests
{
    [AvaloniaFact]
    public async Task Splash_RendersWithoutApplicationStyles_AndDoesNotRestartWhenTheyLoad()
    {
        var styles = Application.Current!.Styles.ToArray();
        Application.Current.Styles.Clear();
        var splash = new SplashWindow();
        try
        {
            splash.Show();
            splash.UpdateLayout();
            var wheel = Assert.Single(splash.GetVisualDescendants().OfType<Avalonia.Controls.Image>());
            Assert.NotNull(wheel.Source);
            Assert.Equal(220, wheel.Bounds.Width);
            Assert.Equal(220, wheel.Bounds.Height);
            var entrance = splash.FindControl<Avalonia.Controls.Grid>("Entrance")!;
            var visual = ElementComposition.GetElementVisual(wheel);
            Assert.NotNull(visual);
            Assert.Equal(new Vector3D(110, 110, 0), visual.CenterPoint);
            await Task.Delay(250);
            Assert.Equal(1, entrance.Opacity);
            foreach (var style in styles)
                Application.Current.Styles.Add(style);
            splash.UpdateLayout();
            Assert.Equal(1, entrance.Opacity);
            Assert.Same(visual, ElementComposition.GetElementVisual(wheel));
        }
        finally
        {
            splash.Close();
            Application.Current.Styles.Clear();
            foreach (var style in styles)
                Application.Current.Styles.Add(style);
        }
    }

    [AvaloniaTheory]
    [InlineData("ready")]
    [InlineData("timeout")]
    [InlineData("shutdown")]
    public async Task MainWindowAndPages_ConstructWithoutStaticServiceInitialization_AndRefreshSafely(string completion)
    {
        var fileSystem = new MockFileSystem();
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-ui-composition");
        fileSystem.Directory.CreateDirectory(directory);
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(directory);
        var registrations = new ServiceCollection();
        registrations.AddWheelWizardServices(location);
        registrations.AddSingleton<IFileSystem>(fileSystem);
        registrations.AddTransient(typeof(IApiCaller<>), typeof(OfflineApiCaller<>));
        var modsLoaded = new TaskCompletionSource<OperationResult>();
        var mods = Substitute.For<IModManager>();
        mods.ReloadAsync().Returns(modsLoaded.Task);
        registrations.AddSingleton(mods);
        TimerCallback? expire = null;
        object? timerState = null;
        var time = Substitute.For<System.TimeProvider>();
        time.CreateTimer(Arg.Any<TimerCallback>(), Arg.Any<object?>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>())
            .Returns(call =>
            {
                expire = call.ArgAt<TimerCallback>(0);
                timerState = call.ArgAt<object?>(1);
                Assert.Equal(TimeSpan.FromSeconds(10), call.ArgAt<TimeSpan>(2));
                return Substitute.For<ITimer>();
            });
        registrations.AddSingleton<IMainWindowService>(provider => new MainWindowService(
            () => provider.GetRequiredService<Layout>(),
            time
        ));
        using var services = registrations.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
        // The memory filesystem rejects empty Exists paths; supply explicit optional Dolphin paths.
        var settings = services.GetRequiredService<ISettingsManager>();
        settings.NAND_ROOT_PATH.Set(directory, skipSave: true);
        settings.LOAD_PATH.Set(directory, skipSave: true);
        services.GetRequiredService<MiiControlThemes>().Install(Application.Current!.Resources);
        services.GetRequiredService<WindowAppearance>().Install(Application.Current.Resources);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var windows = services.GetRequiredService<IMainWindowService>();
        try
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();
            Assert.True(splash.IsVisible);
            using var shutdown = new CancellationTokenSource();
            var opening = windows.ShowAsync(desktop, shutdown.Token);
            if (opening.IsFaulted)
                await opening;
            Assert.False(opening.IsCompleted);
            Assert.True(splash.IsVisible);
            Assert.Equal(0, desktop.MainWindow!.Opacity);
            Assert.False(desktop.MainWindow.ShowInTaskbar);
            if (completion == "shutdown")
            {
                shutdown.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
                Assert.Equal(0, desktop.MainWindow.Opacity);
                Assert.True(splash.IsVisible);
                modsLoaded.SetResult(OperationResult.Ok());
                splash.Close();
                return;
            }
            if (completion == "timeout")
                expire!(timerState);
            else
                modsLoaded.SetResult(OperationResult.Ok());
            await opening;
            Assert.False(splash.IsVisible);
            Assert.Equal(1, desktop.MainWindow.Opacity);
            Assert.True(desktop.MainWindow.ShowInTaskbar);
            if (completion == "timeout")
            {
                Assert.False(modsLoaded.Task.IsCompleted);
                modsLoaded.SetResult(OperationResult.Ok());
            }
            var original = Assert.IsType<Layout>(desktop.MainWindow);
            original.UpdateLayout();
            Assert.Equal(Layout.WindowHeight * settings.Get<double>(settings.WINDOW_SCALE), original.Height);
            {
                var content = original.FindControl<Grid>("CompleteGrid")!;
                Assert.Equal(0, content.RowDefinitions[0].Height.Value);
                var frame = Assert.IsType<Grid>(original.Content);
                var titleBar = Assert.IsType<Border>(frame.Children[0]);
                Assert.NotNull(titleBar.Background);
                Assert.Equal(
                    Avalonia.Input.WindowDecorationsElementRole.TitleBar,
                    Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(titleBar)
                );
                var logo = original.FindControl<IconLabel>("TitleLabel")!;
                Assert.Equal(16, logo.FontSize);
                Assert.Equal(20, logo.IconSize);
                Assert.Equal(string.Empty, logo.Text);
                Assert.Equal(!OperatingSystem.IsMacOS(), Assert.IsType<StackPanel>(logo.Parent).IsVisible);
                Assert.Same(titleBar, Assert.IsType<Grid>(logo.Parent!.Parent).Parent);
                var logoPosition = logo.TranslatePoint(default, titleBar)!.Value;
                Assert.Equal(logoPosition.X, logoPosition.Y);
                Assert.Null(original.FindControl<Button>("HeaderBackButton"));
                Assert.Null(original.FindControl<Button>("HeaderForwardButton"));
                if (!OperatingSystem.IsMacOS())
                    Assert.Same(Application.Current!.FindResource("DesktopWindowDecorations"), original.WindowDecorationsTheme);
                original.SetInteractable(false);
                original.UpdateLayout();
                var overlay = original.FindControl<Border>("DisabledDarkenEffect")!;
                Assert.Contains(overlay, frame.Children);
                Assert.Equal(frame.Bounds.Height, overlay.Bounds.Height);
                Assert.True(overlay.IsVisible);
                original.SetInteractable(true);
                Assert.False(overlay.IsVisible);
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var navigation = services.GetRequiredService<INavigationService>();
            Assert.IsType<HomePage>(navigation.CurrentPage);

            // Collapse reuses the existing controls and must not resize the window or navigate.
            settings.ENABLE_ANIMATIONS.Set(false, skipSave: true);
            var toggle = original.FindControl<Button>("SidebarToggle")!;
            var originalWidth = original.Width;
            Assert.False(settings.SIDEBAR_COLLAPSED.Get());
            Assert.Equal(221, original.SidebarWidth);
            var titleDivider = navigation.CurrentPage!.GetVisualDescendants().OfType<Border>().First(border => border.Height == 1);
            var dividerCenter = titleDivider.TranslatePoint(new Point(0, titleDivider.Bounds.Height / 2), original)!.Value.Y;
            var toggleCenter = toggle.TranslatePoint(new Point(0, toggle.Bounds.Height / 2), original)!.Value.Y;
            Assert.Equal(dividerCenter, toggleCenter, 1);
            // Image positions follow intermediate widths, not just the collapsed/expanded state.
            original.SidebarWidth = (221d + 64d) / 2;
            Assert.Equal(-12, original.FindControl<MiiAnimatedImage>("SidebarMii")!.Margin.Left);
            Assert.Equal(5, original.FindControl<IconLabel>("TitleLabel")!.Margin.Left);
            Assert.Equal(
                19.5,
                original.FindControl<SidebarRadioButton>("RoomsButton")!.GetVisualDescendants().OfType<IconLabel>().Single().Margin.Left
            );
            original.SidebarWidth = 221;
            var roomsTop = original.FindControl<SidebarRadioButton>("RoomsButton")!.Bounds.Top;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            original.UpdateLayout();
            Assert.Equal(64, original.SidebarWidth);
            Assert.True(settings.SIDEBAR_COLLAPSED.Get());
            Assert.Equal(originalWidth, original.Width);
            Assert.IsType<HomePage>(navigation.CurrentPage);
            Assert.False(original.FindControl<Control>("SupportUsButton")!.IsVisible);
            Assert.True(original.FindControl<MenuItem>("CollapsedSupportMenuItem")!.IsVisible);
            Assert.Equal(18, original.FindControl<Border>("LiveStatusBorder")!.Margin.Left);
            Assert.Equal(1, original.FindControl<Border>("LiveStatusBorder")!.Opacity);
            Assert.Equal(2, Grid.GetRow(original.FindControl<Border>("SidebarInfoButton")!));
            Assert.Equal(0, Grid.GetRow(original.FindControl<Border>("SidebarSettingsButton")!));
            var roomsButton = original.FindControl<SidebarRadioButton>("RoomsButton")!;
            Assert.Equal(roomsTop, roomsButton.Bounds.Top);
            Assert.Null(ToolTip.GetTip(original.FindControl<Border>("SidebarInfoButton")!));
            Assert.Equal(PlacementMode.Right, ToolTip.GetPlacement(roomsButton));
            Assert.Equal(PlacementMode.Right, ToolTip.GetPlacement(original.FindControl<Border>("SidebarSettingsButton")!));
            foreach (var placement in new[] { PlacementMode.Left, PlacementMode.Right })
            {
                var target = new Border();
                var bubble = new ToolTip { Content = "Side tooltip" };
                ToolTip.SetPlacement(target, placement);
                ToolTip.SetTip(target, bubble);
                Assert.Equal(placement, ToolTip.GetPlacement(target));
                Assert.Contains(placement == PlacementMode.Left ? "BubbleSideLeft" : "BubbleSideRight", bubble.Classes);
            }
            Assert.Equal(roomsButton.Text, ToolTip.GetTip(roomsButton));
            Assert.Equal(string.Empty, roomsButton.GetVisualDescendants().OfType<IconLabel>().Single().Text);
            Assert.Equal(19.5, roomsButton.GetVisualDescendants().OfType<IconLabel>().Single().Margin.Left);
            Assert.Equal(0, original.FindControl<TextBlock>("OtherSectionText")!.Opacity);
            Assert.False(roomsButton.GetVisualDescendants().OfType<StatusBadge>().Single().IsVisible);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            original.UpdateLayout();
            Assert.Equal(221, original.SidebarWidth);
            Assert.False(settings.SIDEBAR_COLLAPSED.Get());
            Assert.Equal(originalWidth, original.Width);
            Assert.True(original.FindControl<Control>("SupportUsButton")!.IsVisible);
            Assert.False(original.FindControl<MenuItem>("CollapsedSupportMenuItem")!.IsVisible);
            Assert.Equal(roomsButton.Text, roomsButton.GetVisualDescendants().OfType<IconLabel>().Single().Text);
            Assert.Equal(19.5, roomsButton.GetVisualDescendants().OfType<IconLabel>().Single().Margin.Left);
            Assert.Equal(1, original.FindControl<TextBlock>("OtherSectionText")!.Opacity);
            Assert.Equal(PlacementMode.Top, ToolTip.GetPlacement(original.FindControl<Border>("SidebarSettingsButton")!));
            settings.ENABLE_ANIMATIONS.Set(true, skipSave: true);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var frame = 0; frame < 150 && !toggle.IsEnabled; frame++)
                await Task.Delay(20);
            Assert.True(toggle.IsEnabled);
            Assert.Equal(64, original.SidebarWidth);
            Assert.Equal(1, original.FindControl<Grid>("SidebarBottomBar")!.Opacity);
            settings.ENABLE_ANIMATIONS.Set(false, skipSave: true);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // Settings locks the main sidebar while keeping navigation inside the page body.
            foreach (var initiallyCollapsed in new[] { false, true })
            {
                if (initiallyCollapsed)
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                navigation.NavigateTo<SettingsPage>();
                original.UpdateLayout();
                Assert.Equal(64, original.SidebarWidth);
                Assert.Equal(initiallyCollapsed, settings.SIDEBAR_COLLAPSED.Get());
                Assert.True(toggle.IsVisible);
                Assert.False(toggle.IsEnabled);
                Assert.True(original.FindControl<PathIcon>("SidebarLock")!.IsVisible);
                Assert.False(original.FindControl<PathIcon>("SidebarChevron")!.IsVisible);
                Assert.Equal(1, toggle.Opacity);
                var settingsPage = Assert.IsType<SettingsPage>(navigation.CurrentPage);
                var sidebarSurface = settingsPage.FindControl<Border>("SettingsNavigation")!;
                var settingsBody = settingsPage.FindControl<Grid>("SettingsBody")!;
                Assert.Same(settingsBody, sidebarSurface.Parent);
                Assert.Equal(new Thickness(0, 0, 1, 0), sidebarSurface.BorderThickness);
                Assert.Equal(0, sidebarSurface.CornerRadius.TopLeft);
                Assert.Equal(157, sidebarSurface.Bounds.Width);
                var settingsContent = settingsPage.FindControl<ContentControl>("SettingsContent")!;
                var contentX = settingsContent.TranslatePoint(new Point(), original)!.Value.X;
                foreach (var width in new[] { 221d, 180d, 120d, 64d })
                {
                    original.SidebarWidth = width;
                    original.UpdateLayout();
                    Assert.Equal(221 - width, sidebarSurface.Bounds.Width);
                    Assert.Equal(contentX, settingsContent.TranslatePoint(new Point(), original)!.Value.X, precision: 1);
                }
                Assert.True(sidebarSurface.ClipToBounds);
                var headerDivider = settingsPage
                    .FindControl<Grid>("SettingsRoot")!
                    .Children.OfType<Border>()
                    .Single(border => Grid.GetRow(border) == 0);
                var dividerY = headerDivider.TranslatePoint(new Point(), original)!.Value.Y;
                var firstSettingsButton = sidebarSurface.GetVisualDescendants().OfType<RadioButton>().First();
                Assert.True(firstSettingsButton.TranslatePoint(new Point(), original)!.Value.Y >= dividerY);
                var settingsTitle = settingsPage.FindControl<TextBlock>("SettingsTitle")!;
                Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, settingsTitle.VerticalAlignment);
                Assert.Equal(
                    Avalonia.Layout.VerticalAlignment.Bottom,
                    settingsPage.FindControl<WheelWizard.Views.Components.Button>("DevButton")!.VerticalAlignment
                );
                var sidebarAbout = sidebarSurface
                    .GetVisualDescendants()
                    .OfType<RadioButton>()
                    .Single(button => button.Tag?.ToString() == "AppInfo");
                sidebarAbout.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsType<AppInfo>(settingsPage.FindControl<ContentControl>("SettingsContent")!.Content);
                // Navigating between settings destinations must preserve the original sidebar state.
                navigation.NavigateTo<SettingsPage>(typeof(AppInfo));
                navigation.NavigateTo<HomePage>();
                original.UpdateLayout();
                Assert.Equal(initiallyCollapsed ? 64 : 221, original.SidebarWidth);
                Assert.True(toggle.IsEnabled);
                Assert.False(original.FindControl<PathIcon>("SidebarLock")!.IsVisible);
                Assert.True(toggle.IsVisible);
                if (initiallyCollapsed)
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            Type[] pages =
            [
                typeof(FriendsPage),
                typeof(RoomsPage),
                typeof(ModsPage),
                typeof(MiiListPage),
                typeof(LeaderboardPage),
                typeof(UserProfilePage),
                typeof(TestingPage),
                typeof(SettingsPage),
            ];
            foreach (var page in pages)
            {
                navigation.NavigateTo(page);
                original.UpdateLayout();
                Assert.IsType(page, navigation.CurrentPage);
                if (navigation.CurrentPage is ModsPage modsPage)
                {
                    modsPage.HasMods = true;
                    original.UpdateLayout();
                    var gridView = modsPage.FindControl<MultiIconRadioButton>("GridViewButton")!;
                    var listView = modsPage.FindControl<MultiIconRadioButton>("ListViewButton")!;
                    var list = modsPage.FindControl<ListBox>("ModsListBox")!;
                    var grid = modsPage.FindControl<ScrollViewer>("ModsGridView")!;
                    gridView.IsChecked = true;
                    Assert.True(grid.IsVisible);
                    Assert.False(list.IsVisible);
                    listView.IsChecked = true;
                    Assert.True(list.IsVisible);
                    Assert.False(grid.IsVisible);
                    original.UpdateLayout();
                    var viewToggle = modsPage.FindControl<Border>("ViewModeToggle")!;
                    Assert.True(
                        viewToggle.TranslatePoint(new Point(), modsPage)!.Value.Y > list.TranslatePoint(new Point(), modsPage)!.Value.Y
                    );
                    var enableAll = modsPage.FindControl<CheckBox>("EnableAllCheckbox")!;
                    Assert.True(
                        enableAll.TranslatePoint(new Point(), modsPage)!.Value.Y < list.TranslatePoint(new Point(), modsPage)!.Value.Y
                    );
                    var presenter = list.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ItemsPresenter>().Single();
                    Assert.True(presenter.Margin.Bottom >= viewToggle.Bounds.Height + 12);
                    var icon = listView.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
                    Assert.Equal(Avalonia.Media.Brushes.Transparent, icon.Fill);
                    Assert.NotNull(icon.Stroke);
                }
            }
            var room = new WheelWizard.Models.RRInfo.RrRoom
            {
                Id = "room-test",
                Created = DateTime.UtcNow,
                Type = "private",
                Suspend = false,
                Players = [],
            };
            navigation.NavigateTo<RoomDetailsPage>(room);
            original.UpdateLayout();
            Assert.Same(room, Assert.IsType<RoomDetailsPage>(navigation.CurrentPage).Room);
            Type[] settingsPages =
            [
                typeof(WhWzSettings),
                typeof(VideoSettings),
                typeof(OtherSettings),
                typeof(RecompSettings),
                typeof(AppInfo),
            ];
            foreach (var page in settingsPages)
            {
                navigation.NavigateTo<SettingsPage>(page);
                original.UpdateLayout();
                Assert.IsType<SettingsPage>(navigation.CurrentPage);
            }
            var about = Assert.IsType<SettingsPage>(navigation.CurrentPage);
            var tabs = about.FindControl<StackPanel>("SettingPages")!;
            tabs.Children.OfType<RadioButton>()
                .Single(tab => Equals(tab.Tag, nameof(OtherSettings)))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var other = Assert.IsType<SettingsPage>(navigation.CurrentPage);
            Assert.IsType<OtherSettings>(other.FindControl<ContentControl>("SettingsContent")!.Content);
            navigation.NavigateTo<HomePage>();
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            windows.Refresh();
            Assert.Equal(64, Assert.IsType<Layout>(desktop.MainWindow).SidebarWidth);
            Assert.Equal(string.Empty, desktop.MainWindow!.FindControl<IconLabel>("TitleLabel")!.Text);
            Assert.NotSame(original, desktop.MainWindow);
            Assert.False(original.IsVisible);
            Assert.True(desktop.MainWindow!.IsVisible);
            Assert.Equal(Avalonia.Controls.ShutdownMode.OnMainWindowClose, desktop.ShutdownMode);
        }
        finally
        {
            desktop.MainWindow?.Close();
            desktop.MainWindow = null;
        }
    }

    public sealed class OfflineApiCaller<TApi> : IApiCaller<TApi>
        where TApi : class
    {
        public Task<OperationResult<TResult>> CallApiAsync<TResult>(Expression<Func<TApi, Task<TResult>>> apiCall) =>
            Task.FromResult((OperationResult<TResult>)new OperationError { Message = "Offline UI test" });
    }
}
