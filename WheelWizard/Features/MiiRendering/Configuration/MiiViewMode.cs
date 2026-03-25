namespace WheelWizard.MiiRendering.Configuration;

public readonly record struct MiiLightingProfile(
    float AmbientScale,
    float DirectionalLightInfluence,
    float DiffuseScale,
    float DiffuseFloor,
    float SpecularScale,
    float RimScale,
    float RimPower
);

public static class MiiLightingProfiles
{
    public static readonly MiiLightingProfile Default = new(
        AmbientScale: 0.71f,
        DirectionalLightInfluence: 0.32f,
        DiffuseScale: 1.32f,
        DiffuseFloor: 0.23f,
        SpecularScale: 0.78f,
        RimScale: 0.88f,
        RimPower: 2.56f
    );
}
