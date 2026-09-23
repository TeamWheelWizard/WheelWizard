namespace WheelWizard.CloudSync.Profile;

public interface ICloudProfileService
{
    Task<CloudProfilePackage> CaptureProfileAsync();
    Task ApplyProfileAsync(CloudProfilePackage profile);
    Task ValidateProfileAsync(CloudProfilePackage profile);
    Task WritePackageAsync(CloudProfilePackage profile, string zipPath);
    Task<CloudProfilePackage> ReadPackageAsync(string zipPath);
}
