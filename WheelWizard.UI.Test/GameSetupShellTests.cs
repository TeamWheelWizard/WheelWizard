using System.IO.Abstractions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.GameFiles.Views;
using WheelWizard.MiiImages.Views;
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.Settings.Views;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Views;

namespace WheelWizard.UI.Test;

public class GameSetupShellTests
{
    [AvaloniaFact]
    public async Task AnUpgradingUser_IsHeldOnTheGameSetupPage_WithOnlySettingsReachable()
    {
        var fileSystem = new MockFileSystem();
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-ui-game-setup");
        fileSystem.Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "Mario Kart Wii.rvz");
        fileSystem.File.WriteAllText(image, "an image from before the game was extracted");
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(directory);
        var registrations = new ServiceCollection();
        registrations.AddWheelWizardServices(location);
        registrations.AddSingleton<IFileSystem>(fileSystem);
        registrations.AddTransient(typeof(IApiCaller<>), typeof(ApplicationCompositionTests.OfflineApiCaller<>));
        var mods = Substitute.For<IModManager>();
        mods.ReloadAsync().Returns(Task.FromResult(OperationResult.Ok()));
        registrations.AddSingleton(mods);
        using var services = registrations.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
        var settings = services.GetRequiredService<ISettingsManager>();
        settings.NAND_ROOT_PATH.Set(directory, skipSave: true);
        settings.LOAD_PATH.Set(directory, skipSave: true);
        Assert.True(settings.GAME_LOCATION.Set(image, skipSave: true));
        settings.ONBOARDING_COMPLETED.Set(true, skipSave: true);
        services.GetRequiredService<MiiControlThemes>().Install(Application.Current!.Resources);
        services.GetRequiredService<WindowAppearance>().Install(Application.Current.Resources);

        var window = services.GetRequiredService<Layout>();
        var navigation = services.GetRequiredService<INavigationService>();
        try
        {
            window.Show();
            window.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.IsType<GameSetupPage>(navigation.CurrentPage);
            Assert.False(window.FindControl<Grid>("SidebarGrid")!.IsEnabled);

            // The sidebar and every other way to Home stay closed.
            navigation.NavigateTo<HomePage>();
            Assert.IsType<GameSetupPage>(navigation.CurrentPage);
            navigation.IsLocked = false;
            navigation.NavigateAway(typeof(HomePage));
            Assert.IsType<GameSetupPage>(navigation.CurrentPage);

            // Settings is the way out to free up space or move the data folder (what "Open Settings" does), and
            // leaving it for anything else comes straight back.
            navigation.IsLocked = false;
            navigation.NavigateAway(typeof(SettingsPage));
            Assert.IsType<SettingsPage>(navigation.CurrentPage);
            navigation.NavigateTo<HomePage>();
            Assert.IsType<GameSetupPage>(navigation.CurrentPage);

            Assert.Equal("an image from before the game was extracted", fileSystem.File.ReadAllText(image));
        }
        finally
        {
            navigation.IsLocked = false;
            window.Close();
        }
    }
}
