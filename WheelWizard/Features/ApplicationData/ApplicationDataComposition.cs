using System.IO.Abstractions;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;

namespace WheelWizard.ApplicationData;

public static class ApplicationDataComposition
{
    public static IServiceCollection AddApplicationData(this IServiceCollection services, IApplicationDataLocation? applicationData = null)
    {
        if (applicationData != null)
            services.AddSingleton(applicationData);
        else
            services.AddSingleton(provider =>
                ApplicationDataComposition.CreateLocation(
                    provider.GetRequiredService<IFileSystem>(),
                    provider.GetRequiredService<IRuntimeEnvironment>()
                )
            );
        return services;
    }

    /// <summary>Creates the location before logging starts; the same instance is then registered in DI.</summary>
    public static IApplicationDataLocation CreateLocation(IFileSystem fileSystem, IRuntimeEnvironment environment)
    {
        var directories = new ApplicationDataDirectories(fileSystem, environment);
        IApplicationDataLocationStore store = environment.IsWindows
            ? new WindowsApplicationDataLocationStore()
            : new FileApplicationDataLocationStore(fileSystem, directories);
        return new ApplicationDataLocation(fileSystem, directories, store, new DirectoryTransferService(fileSystem));
    }
}
