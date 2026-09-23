namespace WheelWizard.CloudSync.Mii;

public interface IMiiProfileService
{
    Task<byte[]> ExtractMiiAsync(MiiIdentifier id);
    Task EnsureMiiPresentAsync(byte[] mii);
    Task<bool> IsMiiPresentAsync(MiiIdentifier id);
    Task<ProfileMiiExtraction> ExtractProfileMiiAsync(byte[] rksysData);
}
