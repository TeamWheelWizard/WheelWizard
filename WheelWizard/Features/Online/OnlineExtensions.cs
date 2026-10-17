using WheelWizard.Online.ViewModels;

namespace WheelWizard.Online;

public static class OnlineExtensions
{
    public static IServiceCollection AddOnline(this IServiceCollection services)
    {
        services.AddTransient<OnlineViewModel>();
        return services;
    }
}
