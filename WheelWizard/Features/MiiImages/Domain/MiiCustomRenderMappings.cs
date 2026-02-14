using System.Numerics;
using WheelWizard.MiiImages;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

namespace WheelWizard.MiiImages.Domain;

public static class MiiCustomRenderMappings
{
    public static MiiImageSpecifications WithCustomRenderPreferences(
        this MiiImageSpecifications baseSpecifications,
        CustomMiiDataV1 customDataV1
    )
    {
        var customized = baseSpecifications.Clone();

        customized.BackgroundColor = MiiCustomMappings.GetAccentBackgroundHex(customDataV1.AccentColor);
        customized.Expression = GetExpression(customDataV1.FacialExpression, customized.Expression);

        if (TryGetCameraPreset(customDataV1.CameraAngle, out var characterRotation, out var cameraRotation))
        {
            customized.CharacterRotate = characterRotation;
            customized.CameraRotate = cameraRotation;
        }

        return customized;
    }

    private static MiiImageSpecifications.FaceExpression GetExpression(
        MiiPreferredFacialExpression preferredExpression,
        MiiImageSpecifications.FaceExpression fallback
    )
    {
        return preferredExpression switch
        {
            MiiPreferredFacialExpression.None => fallback,
            MiiPreferredFacialExpression.FacialExpression1 => MiiImageSpecifications.FaceExpression.smile,
            MiiPreferredFacialExpression.FacialExpression2 => MiiImageSpecifications.FaceExpression.smile_open_mouth,
            MiiPreferredFacialExpression.FacialExpression3 => MiiImageSpecifications.FaceExpression.anger,
            MiiPreferredFacialExpression.FacialExpression4 => MiiImageSpecifications.FaceExpression.frustrated,
            MiiPreferredFacialExpression.FacialExpression5 => MiiImageSpecifications.FaceExpression.surprise,
            MiiPreferredFacialExpression.FacialExpression6 => MiiImageSpecifications.FaceExpression.like_wink_left,
            MiiPreferredFacialExpression.FacialExpression7 => MiiImageSpecifications.FaceExpression.sorrow,
            _ => fallback,
        };
    }

    private static bool TryGetCameraPreset(MiiPreferredCameraAngle cameraAngle, out Vector3 characterRotate, out Vector3 cameraRotate)
    {
        switch (cameraAngle)
        {
            case MiiPreferredCameraAngle.CameraAngle1:
                characterRotate = Vector3.Zero;
                cameraRotate = Vector3.Zero;
                return true;
            case MiiPreferredCameraAngle.CameraAngle2:
                characterRotate = new(350, 20, 0);
                cameraRotate = new(10, -5, 0);
                return true;
            case MiiPreferredCameraAngle.CameraAngle3:
                characterRotate = new(350, 340, 0);
                cameraRotate = new(10, 5, 0);
                return true;
            default:
                characterRotate = Vector3.Zero;
                cameraRotate = Vector3.Zero;
                return false;
        }
    }
}
