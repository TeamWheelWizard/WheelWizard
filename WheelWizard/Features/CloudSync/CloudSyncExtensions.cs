using WheelWizard.CloudSync.Backup;
using WheelWizard.CloudSync.Conflict;
using WheelWizard.CloudSync.Credentials;
using WheelWizard.CloudSync.Enrollment;
using WheelWizard.CloudSync.Mii;
using WheelWizard.CloudSync.Profile;
using WheelWizard.CloudSync.ProfileLibrary;
using WheelWizard.CloudSync.Providers;

namespace WheelWizard.CloudSync;

public static class CloudSyncExtensions
{
    public static IServiceCollection AddCloudSync(this IServiceCollection services)
    {
        services.AddHttpClient("WheelWizard.CloudSync.WebDav");
        services.AddHttpClient("WheelWizard.CloudSync.OAuth");
        services.AddSingleton<ISecureCredentialStore, SecureCredentialStore>();
        services.AddSingleton<IMiiProfileService, MiiProfileService>();
        services.AddSingleton<IProfileBackupService, ProfileBackupService>();
        services.AddSingleton<ICloudProfileService, CloudProfileService>();
        services.AddSingleton<ICloudProfileLibraryService, CloudProfileLibraryService>();
        services.AddSingleton<IVirtualProfileVaultService, VirtualProfileVaultService>();
        services.AddSingleton<IVirtualProfileCloudService, VirtualProfileCloudService>();
        services.AddSingleton<IProfileCloudBindingService, ProfileCloudBindingService>();
        services.AddSingleton<IVisibleProfileLaunchService, VisibleProfileLaunchService>();
        services.AddSingleton<ICloudConflictResolver, CloudConflictResolver>();
        services.AddSingleton<IRetroWfcEnrollmentService, RetroWfcEnrollmentService>();
        services.AddSingleton<ICloudProvider, WebDavProvider>();
        services.AddSingleton<ICloudProvider, NextcloudProvider>();
        services.AddSingleton<ICloudProvider, GoogleDriveProvider>();
        services.AddSingleton<ICloudProvider, OneDriveProvider>();
        services.AddSingleton<ICloudProviderResolver, CloudProviderResolver>();
        services.AddSingleton<ICloudSyncService, CloudSyncService>();
        return services;
    }
}
