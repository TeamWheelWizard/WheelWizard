using WheelWizard.MiiImages.Domain;
using WheelWizard.Services;

namespace WheelWizard.MiiImages;

public static class MiiImagesExtensions
{
    public static IServiceCollection AddMiiImages(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IMiiIMagesApi>(Endpoints.MiiImageAddress);

        services.AddSingleton<IMiiImagesSingletonService, MiiImagesSingletonService>();

        return services;
    }

    public static MiiImageSpecifications Clone(this MiiImageSpecifications specifications)
    {
        return new MiiImageSpecifications
        {
            Name = specifications.Name,
            Size = specifications.Size,
            Expression = specifications.Expression,
            Type = specifications.Type,
            BackgroundColor = specifications.BackgroundColor,
            InstanceCount = specifications.InstanceCount,
            CharacterRotate = new(specifications.CharacterRotate.X, specifications.CharacterRotate.Y, specifications.CharacterRotate.Z),
            CameraRotate = new(specifications.CameraRotate.X, specifications.CameraRotate.Y, specifications.CameraRotate.Z),
            ExpirationSeconds =
                specifications.ExpirationSeconds?.TotalSeconds == null
                    ? null
                    : TimeSpan.FromSeconds(specifications.ExpirationSeconds.Value.TotalSeconds),
            CachePriority = specifications.CachePriority,
        };
    }
}
