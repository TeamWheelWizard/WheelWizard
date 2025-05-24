namespace WheelWizard.WiiManagement.Domain.Mii;

public class MiiHair
{
    public int HairType { get; }
    public MiiHairColor MiiHairColor { get; }
    public bool HairFlipped { get; }

    public MiiHair(int hairType, MiiHairColor miiHairColor, bool hairFlipped)
    {
        if (hairType is < 0 or > 71)
            throw new ArgumentException("HairType out of range");
        HairType = hairType;
        MiiHairColor = miiHairColor;
        HairFlipped = hairFlipped;
    }

    public static OperationResult<MiiHair> Create(int hairType, MiiHairColor miiHairColor, bool hairFlipped) =>
        TryCatch(() => new MiiHair(hairType, miiHairColor, hairFlipped));
}
