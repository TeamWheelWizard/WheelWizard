using WheelWizard.Settings.Types;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Domain;

/// <summary>
/// Stands in for a license without a Mii (the game calls it "no name"). Every Mii image draws it locked: grey, with a
/// big question mark for a face and scan lines running over it, a look no real Mii can have.
/// </summary>
public sealed class LockedMii : Mii
{
    // Just the model under the locked look: the default Mii, with a shirt that comes out a mid grey.
    public LockedMii()
    {
        var model = MiiFactory.CreateDefaultMale();
        MiiId = model.MiiId;
        IsGirl = model.IsGirl;
        Height = model.Height;
        Weight = model.Weight;
        MiiFacialFeatures = model.MiiFacialFeatures;
        MiiHair = model.MiiHair;
        MiiEyebrows = model.MiiEyebrows;
        MiiEyes = model.MiiEyes;
        MiiNose = model.MiiNose;
        MiiLips = model.MiiLips;
        MiiGlasses = model.MiiGlasses;
        MiiFacialHair = model.MiiFacialHair;
        MiiMole = model.MiiMole;
        MiiFavoriteColor = MiiFavoriteColor.LightBlue;
    }

    /// <summary><paramref name="mii"/>, or a locked Mii when the license has none.</summary>
    public static Mii For(Mii? mii) => mii is null || mii.Name.ToString() == SettingValues.NoName ? new LockedMii() : mii;
}
