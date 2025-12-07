namespace WheelWizard.WiiManagement.GameLicense.Domain;

public class LicenseCollection
{
    public List<LicenseProfile> Users { get; set; }

    public LicenseCollection()
    {
        Users = new(4);
    }
}
