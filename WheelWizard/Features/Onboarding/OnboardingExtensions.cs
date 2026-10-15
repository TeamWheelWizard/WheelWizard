namespace WheelWizard.Onboarding;

public static class OnboardingExtensions
{
    public static IServiceCollection AddOnboarding(this IServiceCollection services)
    {
        services.AddSingleton<IOnboardingService, OnboardingService>();
        return services;
    }
}
