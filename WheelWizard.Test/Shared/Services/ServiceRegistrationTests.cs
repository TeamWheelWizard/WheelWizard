using Microsoft.Extensions.DependencyInjection;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Shared.Downloads;

namespace WheelWizard.Test.Shared.Services;

public sealed class ServiceRegistrationTests
{
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
