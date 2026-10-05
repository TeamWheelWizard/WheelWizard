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
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views;
using WheelWizard.Views.Components;
using WheelWizard.Views.Navigation;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Pages.Settings;
using WheelWizard.Views.Patterns;
using WheelWizard.Views.Startup;
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

    [AvaloniaFact]
    public async Task MainWindowAndPages_ConstructWithoutStaticServiceInitialization_AndRefreshSafely()
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
            var opening = windows.ShowAsync(desktop);
            if (opening.IsFaulted)
                await opening;
            Assert.False(opening.IsCompleted);
            Assert.True(splash.IsVisible);
            Assert.Equal(0, desktop.MainWindow!.Opacity);
            Assert.False(desktop.MainWindow.ShowInTaskbar);
            modsLoaded.SetResult(OperationResult.Ok());
            await opening;
            Assert.False(splash.IsVisible);
            Assert.Equal(1, desktop.MainWindow.Opacity);
            Assert.True(desktop.MainWindow.ShowInTaskbar);
            var original = Assert.IsType<Layout>(desktop.MainWindow);
            original.UpdateLayout();
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
            Assert.Equal(-12, original.FindControl<MiiImageLoader>("SidebarMii")!.Margin.Left);
            Assert.Equal(13.25, original.FindControl<IconLabel>("TitleLabel")!.Margin.Left);
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
            Assert.False(roomsButton.GetVisualDescendants().OfType<StateBox>().Single().IsVisible);
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
