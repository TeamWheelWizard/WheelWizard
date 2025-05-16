using Testably.Abstractions.RandomSystem;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement;

public static class MiiFactory
{
    private static Mii CreateDefaultBase()
    {
        return new Mii
        {
            Name = new("New Mii"),
            CreatorName = new(""),
            IsFavorite = false,
            MiiFavoriteColor = MiiFavoriteColor.Red,
            MiiFacialFeatures = new(MiiFaceShape.Teardrop, MiiSkinColor.Light, MiiFacialFeature.None, false, false),
            MiiHair = new(
                30 /* bald >:) */
                ,
                MiiHairColor.Brown,
                false
            ),
            MiiEyebrows = new(0, 6, MiiHairColor.Brown, 4, 10, 2),
            MiiGlasses = new(MiiGlassesType.None, MiiGlassesColor.Grey, 4, 10),
            MiiEyes = new(2, 4, 12, MiiEyeColor.Black, 4, 2),
            MiiNose = new(MiiNoseType.SemiCircle, 4, 9),
            MiiLips = new(23, MiiLipColor.Skin, 4, 13),
            MiiFacialHair = new(MiiMustacheType.None, MiiBeardType.None, MiiHairColor.Black, 4, 10),
            MiiMole = new(false, 4, 20, 2),
            Height = new(63),
            Weight = new(63),
            MiiId = 1,
        };
    }

    public static Mii CreateDefaultFemale()
    {
        var baseMii = CreateDefaultBase();
        baseMii.IsGirl = true;
        baseMii.MiiHair = new(12, MiiHairColor.Brown, false);
        baseMii.MiiEyebrows = new(0, 6, MiiHairColor.Brown, 4, 10, 2);
        baseMii.MiiEyes = new(4, 3, 12, MiiEyeColor.Black, 4, 2);
        return baseMii;
    }

    public static Mii CreateDefaultMale()
    {
        var baseMii = CreateDefaultBase();
        baseMii.IsGirl = false;
        baseMii.MiiHair = new(33, MiiHairColor.Brown, false);
        baseMii.MiiEyebrows = new(6, 6, MiiHairColor.Brown, 4, 10, 2);
        baseMii.MiiEyes = new(2, 4, 12, MiiEyeColor.Black, 4, 2);
        return baseMii;
    }

    public static Mii CreateRandomMii(IRandom random)
    {
        var baseMii = CreateDefaultBase();
        var hairColor = (MiiHairColor)(random.Next() % 8);

        baseMii.IsGirl = random.Next() % 2 == 0;
        baseMii.MiiHair = new(random.Next() % 71, hairColor, random.Next() % 3 == 0);
        baseMii.MiiEyebrows = new(random.Next() % 23, 6, hairColor, 4, 10, 2);
        baseMii.MiiEyes = new(random.Next() % 47, 4, 12, (MiiEyeColor)(random.Next() % 6), 4, 2);
        baseMii.MiiFavoriteColor = (MiiFavoriteColor)(random.Next() % 12);
        baseMii.MiiFacialFeatures = new(
            (MiiFaceShape)(random.Next() % 8),
            (MiiSkinColor)(random.Next() % 6),
            (MiiFacialFeature)(random.Next() % 12),
            false,
            false
        );
        baseMii.MiiNose = new((MiiNoseType)(random.Next() % 12), 4, 9);
        baseMii.MiiLips = new(random.Next() % 23, (MiiLipColor)(random.Next() % 3), 4, 13);
        baseMii.MiiMole = new(random.Next() % 4 == 0, 4, 20, 2);
        if (random.Next() % 4 == 0)
            baseMii.MiiGlasses = new((MiiGlassesType)(random.Next() % 9), (MiiGlassesColor)(random.Next() % 6), 4, 10);
        else
            baseMii.MiiGlasses = new(MiiGlassesType.None, MiiGlassesColor.Grey, 4, 10);

        if (random.Next() % 4 == 0)
            baseMii.MiiFacialHair = new((MiiMustacheType)(random.Next() % 4), (MiiBeardType)(random.Next() % 4), hairColor, 4, 10);
        return baseMii;
    }
}
