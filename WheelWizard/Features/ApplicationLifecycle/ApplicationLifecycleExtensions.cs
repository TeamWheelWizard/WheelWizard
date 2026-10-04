using WheelWizard.ApplicationLifecycle.Logging;

namespace WheelWizard.ApplicationLifecycle;

public static class ApplicationLifecycleExtensions
{
    public static IServiceCollection AddApplicationLifecycle(this IServiceCollection services)
    {
        services.AddSingleton<IApplicationStartup, ApplicationStartup>();
        services.AddSingleton<IApplicationLiveUpdates, ApplicationLiveUpdates>();
        services.AddSingleton<ILogFileFactory, LogFileFactory>();
        services.AddSingleton<ApplicationLogFiles>();
        services.AddSingleton<IApplicationLogFiles>(provider => provider.GetRequiredService<ApplicationLogFiles>());
        return services;
    }
}
