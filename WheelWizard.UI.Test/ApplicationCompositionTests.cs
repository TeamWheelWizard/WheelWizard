using System.IO.Abstractions;
using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views;
using WheelWizard.Views.Navigation;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Pages.Settings;
using WheelWizard.Views.Patterns;

namespace WheelWizard.UI.Test;

public class ApplicationCompositionTests
{
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
            windows.Show(desktop);
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
