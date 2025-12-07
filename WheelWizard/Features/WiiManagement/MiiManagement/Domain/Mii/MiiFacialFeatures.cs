namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

public class MiiFacialFeatures
{
    public MiiFaceShape FaceShape { get; }
    public MiiSkinColor SkinColor { get; }
    public MiiFacialFeature FacialFeature { get; }
    public bool MingleOff { get; }
    public bool Downloaded { get; }

    public MiiFacialFeatures(
        MiiFaceShape faceShape,
        MiiSkinColor skinColor,
        MiiFacialFeature facialFeature,
        bool mingleOff,
        bool downloaded
    )
    {
        FaceShape = faceShape;
        SkinColor = skinColor;
        FacialFeature = facialFeature;
        MingleOff = mingleOff;
        Downloaded = downloaded;
    }

    public static OperationResult<MiiFacialFeatures> Create(
        MiiFaceShape faceShape,
        MiiSkinColor skinColor,
        MiiFacialFeature facialFeature,
        bool mingleOff,
        bool downloaded
    ) => TryCatch(() => new MiiFacialFeatures(faceShape, skinColor, facialFeature, mingleOff, downloaded));
}
