﻿namespace WheelWizard.Models.GameData;

public class LicenseCollection
{
    public List<LicenseProfile> Users { get; set; }

    public LicenseCollection()
    {
        Users = new(4);
    }
}
