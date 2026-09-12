namespace WheelWizard.ApplicationIntegration;

public static class ApplicationIntegrationExtensions
{
    public static IServiceCollection AddApplicationIntegration(this IServiceCollection services)
    {
        services.AddSingleton<IUrlProtocolRegistrationStore, WindowsUrlProtocolRegistrationStore>();
        services.AddSingleton<IUrlProtocolRegistration, UrlProtocolRegistration>();
        return services;
    }
}
