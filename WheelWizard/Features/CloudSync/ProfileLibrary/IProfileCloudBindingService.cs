namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>Assigns each physical Mario Kart license slot its own persistent cloud profile ID.</summary>
public interface IProfileCloudBindingService
{
    Task<Guid> GetProfileIdAsync(int localSlot);
}
