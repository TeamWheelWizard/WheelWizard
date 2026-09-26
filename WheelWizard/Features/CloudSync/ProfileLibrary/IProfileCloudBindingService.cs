namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>Assigns each physical Mario Kart license its own persistent cloud profile ID.</summary>
public interface IProfileCloudBindingService
{
    Task<Guid> GetProfileIdAsync(int localSlot, string licenseIdentity);

    /// <summary>Binds an already matched physical slot to its canonical remote profile ID.</summary>
    Task BindAsync(int localSlot, string licenseIdentity, Guid profileId);
}
