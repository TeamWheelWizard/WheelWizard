namespace WheelWizard.Features.Archives;

public static class ArchivesExtensions
{
    public static IServiceCollection AddArchives(this IServiceCollection services)
    {
        services.AddSingleton<ISzsArchiveDecoder, SzsArchiveDecoder>();
        return services;
    }
}
