namespace WheelWizard.DiscImages;

public static class DiscImagesExtensions
{
    public static IServiceCollection AddDiscImages(this IServiceCollection services)
    {
        services.AddSingleton<IDiscImageService, DiscImageService>();
        return services;
    }
}
