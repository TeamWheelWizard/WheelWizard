using System.Globalization;
using System.IO.Abstractions;
using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Localization;
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
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Controls;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Startup;
using WheelWizard.Views.Shell.Views;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement.MiiManagement.Views;

namespace WheelWizard.UI.Test;

public class ApplicationCompositionTests
{
    [AvaloniaTheory]
    [InlineData("ready")]
    [InlineData("timeout")]
    [InlineData("shutdown")]
    public async Task MainWindowAndPages_ConstructWithoutStaticServiceInitialization_AndChangeLanguageInPlace(string completion)
    {
        var previousLocalization = LocalizationProvider.Current;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
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
        PopupWindow? confirmation = null;
        using var popupOpened = Window.WindowOpenedEvent.AddClassHandler<PopupWindow>((popup, _) => confirmation = popup);
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
            // The shell is fixed-size on every platform, including Linux tiling desktops.
            Assert.False(original.CanResize);
            Assert.False(original.CanMaximize);
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
            var sidebar = original.FindControl<StackPanel>("SidePanelButtons")!;
            var sidebarBounds = sidebar.Bounds;
            var sidebarPosition = sidebar.TranslatePoint(default, original);
            foreach (var page in pages)
            {
                navigation.NavigateTo(page);
                original.UpdateLayout();
                Assert.IsType(page, navigation.CurrentPage);
                if (page == typeof(LeaderboardPage))
                {
                    Assert.Equal(sidebarBounds, sidebar.Bounds);
                    Assert.Equal(sidebarPosition, sidebar.TranslatePoint(default, original));
                    typeof(LeaderboardPage).GetProperty(nameof(LeaderboardPage.HasData))!.SetValue(navigation.CurrentPage, true);
                    original.UpdateLayout();
                    Assert.Equal(sidebarBounds, sidebar.Bounds);
                    Assert.Equal(sidebarPosition, sidebar.TranslatePoint(default, original));
                }
            }
            navigation.NavigateTo<SettingsPage>(typeof(AppearanceSettings));
            original.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var settingsPage = Assert.IsType<SettingsPage>(navigation.CurrentPage);
            var settingsContent = settingsPage.FindControl<ContentControl>("SettingsContent")!;
            var appearance = Assert.IsType<AppearanceSettings>(settingsContent.Content);
            var languages = appearance.FindControl<Dropdown>("WhWzLanguageDropdown")!;
            Assert.Equal("Settings", settingsPage.FindControl<TextBlock>("SettingsTitle")!.Text);
            // Cancel leaves both the setting and the selected language unchanged.
            languages.SelectedIndex = 1;
            var cancelPopup = Assert.IsType<PopupWindow>(confirmation);
            var cancelQuestion = Assert.IsType<YesNoWindow>(cancelPopup.FindControl<ContentPresenter>("PopupContent")!.Content);
            cancelQuestion.FindControl<ActionButton>("NoButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal("en", settings.WW_LANGUAGE.Value);
            Assert.Equal(0, languages.SelectedIndex);

            foreach (var language in new[] { "nl", "de", "en" })
            {
                languages.SelectedItem = languages.Items.Single(item =>
                    item!.ToString() == LocalizationProvider.GetLanguageDisplayName(language)
                );
                var popup = Assert.IsType<PopupWindow>(confirmation);
                var question = Assert.IsType<YesNoWindow>(popup.FindControl<ContentPresenter>("PopupContent")!.Content);
                question.FindControl<ActionButton>("YesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                original.UpdateLayout();
                Assert.Equal(language, settings.WW_LANGUAGE.Value);
                Assert.Equal(language, LocalizationProvider.Current.CurrentLanguage);
                Assert.Same(original, desktop.MainWindow);
                Assert.True(original.IsVisible);
                Assert.True(original.CompleteContentEnabled);
                Assert.Same(settingsPage, navigation.CurrentPage);
                Assert.Same(appearance, settingsContent.Content);
                Assert.Equal(
                    LocalizationProvider.Translate("page_title.settings"),
                    settingsPage.FindControl<TextBlock>("SettingsTitle")!.Text
                );
                Assert.Contains(
                    appearance.GetVisualDescendants().OfType<FormFieldLabel>(),
                    label => label.Text == LocalizationProvider.Translate("option.wh_wz_language")
                );
                Assert.Equal(LocalizationProvider.Translate("component.dropdown.search_placeholder"), languages.SearchPlaceholder);
                Assert.Equal(LocalizationProvider.Translate("component.dropdown.empty"), languages.EmptyText);
            }
            Assert.Equal(Avalonia.Controls.ShutdownMode.OnMainWindowClose, desktop.ShutdownMode);
            desktop.DidNotReceive().Shutdown(Arg.Any<int>());
        }
        finally
        {
            desktop.MainWindow?.Close();
            desktop.MainWindow = null;
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = previousDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
            LocalizationProvider.Use(previousLocalization);
        }
    }

    public sealed class OfflineApiCaller<TApi> : IApiCaller<TApi>
        where TApi : class
    {
        public Task<OperationResult<TResult>> CallApiAsync<TResult>(Expression<Func<TApi, Task<TResult>>> apiCall) =>
            Task.FromResult((OperationResult<TResult>)new OperationError { Message = "Offline UI test" });
    }
}
