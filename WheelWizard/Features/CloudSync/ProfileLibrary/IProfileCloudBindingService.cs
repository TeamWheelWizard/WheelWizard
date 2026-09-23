namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>Assigns each physical Mario Kart license slot its own persistent cloud profile ID.</summary>
public interface IProfileCloudBindingService
{
    Task<Guid> GetProfileIdAsync(int localSlot);

    /// <summary>Binds an already matched physical slot to its canonical remote profile ID.</summary>
    Task BindAsync(int localSlot, Guid profileId);
}
