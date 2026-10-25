using System.IO.Abstractions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.MiiImages.Views;
using WheelWizard.Mods;
using WheelWizard.Onboarding.Views;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Views;

namespace WheelWizard.UI.Test;

public class OnboardingShellTests
{
    [AvaloniaFact]
    public async Task FreshInstall_OpensOnboarding_WithTheRestOfTheAppOutOfReach()
    {
        var fileSystem = new MockFileSystem();
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-ui-onboarding");
        fileSystem.Directory.CreateDirectory(directory);
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(directory);
        var registrations = new ServiceCollection();
        registrations.AddWheelWizardServices(location);
        registrations.AddSingleton<IFileSystem>(fileSystem);
        registrations.AddTransient(typeof(IApiCaller<>), typeof(ApplicationCompositionTests.OfflineApiCaller<>));
        var mods = Substitute.For<IModManager>();
        mods.ReloadAsync().Returns(Task.FromResult(OperationResult.Ok()));
        registrations.AddSingleton(mods);
        var download = Substitute.For<IRetroRewindBackgroundInstall>();
        download.State.Returns(BackgroundInstallState.Running);
        download.IsRunning.Returns(true);
        download.Percent.Returns(42);
        download.StartAsync().Returns(new TaskCompletionSource<OperationResult>().Task);
        registrations.AddSingleton(download);
        using var services = registrations.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
        var settings = services.GetRequiredService<ISettingsManager>();
        settings.NAND_ROOT_PATH.Set(directory, skipSave: true);
        settings.LOAD_PATH.Set(directory, skipSave: true);
        services.GetRequiredService<MiiControlThemes>().Install(Application.Current!.Resources);
        services.GetRequiredService<WindowAppearance>().Install(Application.Current.Resources);

        var window = services.GetRequiredService<Layout>();
        try
        {
            window.Show();
            window.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var navigation = services.GetRequiredService<INavigationService>();
            var page = Assert.IsType<OnboardingPage>(navigation.CurrentPage);
            Assert.False(window.FindControl<Grid>("SidebarGrid")!.IsEnabled);
            await download.Received(1).StartAsync();
            Assert.Contains("42%", page.FindControl<TextBlock>("RetroRewindText")!.Text);

            navigation.NavigateTo<HomePage>();
            Assert.IsType<OnboardingPage>(navigation.CurrentPage);
        }
        finally
        {
            services.GetRequiredService<INavigationService>().IsLocked = false;
            window.Close();
        }
    }
}
