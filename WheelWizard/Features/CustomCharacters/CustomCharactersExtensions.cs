namespace WheelWizard.CustomCharacters;

public static class CustomCharactersExtensions
{
    public static IServiceCollection AddCustomCharacters(this IServiceCollection services)
    {
        services.AddSingleton<ICustomCharactersService, CustomCharactersService>();

        return services;
    }
}
