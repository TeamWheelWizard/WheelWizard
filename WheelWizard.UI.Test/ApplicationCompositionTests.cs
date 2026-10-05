using System.IO.Abstractions;
using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
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
using WheelWizard.Views.Navigation;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Pages.Settings;
using WheelWizard.Views.Patterns;
using WheelWizard.Views.Startup;

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
            windows.Refresh();
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
