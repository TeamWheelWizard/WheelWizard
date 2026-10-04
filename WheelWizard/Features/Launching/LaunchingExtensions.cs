using WheelWizard.Recomp;

namespace WheelWizard.Launching;

public static class LaunchingExtensions
{
    public static IServiceCollection AddLaunching(this IServiceCollection services)
    {
        services.AddSingleton<IRetroRewindLaunchDescriptor, RetroRewindLaunchDescriptor>();
        services.AddSingleton<IDolphinLaunchService, DolphinLaunchService>();
        services.AddSingleton<IRetroRewindLaunchService, RetroRewindLaunchService>();
        services.AddSingleton<Func<RrLauncher>>(provider => () => provider.GetRequiredService<RrLauncher>());
        services.AddSingleton<Func<RecompLauncher?>>(provider => () => provider.GetService<RecompLauncher>());
        services.AddTransient<RrLauncher>();
        services.AddTransient<RrBetaLauncher>();
        services.AddSingleton<ILauncherProvider, LauncherProvider>();
        return services;
    }
}
