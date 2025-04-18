﻿using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Models.GameData;

public class MiiData
{
    public Mii? Mii { get; set; }
    public uint AvatarId { get; set; }
    public uint ClientId { get; set; }
}
