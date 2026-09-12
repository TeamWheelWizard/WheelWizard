using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Dolphin.Paths;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.Mods;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Shared.Services;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void BootstrapLocation_IsTheSameInstanceUsedByFeaturePaths_AfterRelocation()
    {
        var location = Substitute.For<IApplicationDataLocation>();
        var directory = Path.Combine(Path.GetTempPath(), "bootstrap-data");
        location.DirectoryPath.Returns(_ => directory);
        var services = new ServiceCollection();
        services.AddWheelWizardServices(location);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var mods = provider.GetRequiredService<IModPaths>();
        var rendering = provider.GetRequiredService<IMiiRenderingPaths>();

        Assert.Same(location, provider.GetRequiredService<IApplicationDataLocation>());
        Assert.Equal(Path.Combine(directory, "Mods", "Temp"), mods.DownloadFolderPath);
        directory = Path.Combine(Path.GetTempPath(), "relocated-data");
        Assert.Equal(Path.Combine(directory, "Mods", "Temp"), mods.DownloadFolderPath);
        Assert.Equal(Path.Combine(directory, "MiiRendering", "FFLResHigh.dat"), rendering.ManagedResourcePath);
    }

    [Fact]
    public void DefaultLocationComposition_UsesRegisteredFilesystemAndEnvironment()
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(true);
        environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns("/settings");
        fs.Directory.CreateDirectory("/settings");
        var services = new ServiceCollection();
        services.AddWheelWizardServices();
        services.AddSingleton<IFileSystem>(fs);
        services.AddSingleton(environment);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Equal("/settings/CT-MKWII", provider.GetRequiredService<IApplicationDataLocation>().DirectoryPath);
    }

    [Fact]
    public void ApplicationRegistrations_ValidateAllConstructorDependencies()
    {
        var services = new ServiceCollection();
        services.AddWheelWizardServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<DownloadService>(provider.GetRequiredService<IDownloadService>());
        Assert.IsType<DolphinPathResolver>(provider.GetRequiredService<IDolphinPathResolver>());
    }
}
