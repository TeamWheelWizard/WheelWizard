using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiRendering.Realtime;

/// <summary>A part of the head that can be highlighted or swapped with a transition on its own.</summary>
public enum HeadPart
{
    /// <summary>Everything (e.g. a new face shape changes the whole head).</summary>
    Head,

    /// <summary>Just the face skin (its texture holds wrinkles and make-up).</summary>
    Faceline,
    Hair,
    Nose,
    Beard,
    Glasses,
    Eyes,
    Eyebrows,
    Mouth,
    Mustache,
    Mole,
}

public static class HeadParts
{
    /// <summary>The face mask layer the part is painted in, or <see cref="MiiMaskLayers.None"/> for a mesh part.</summary>
    public static MiiMaskLayers MaskLayer(this HeadPart part) =>
        part switch
        {
            HeadPart.Eyes => MiiMaskLayers.Eyes,
            HeadPart.Eyebrows => MiiMaskLayers.Eyebrows,
            HeadPart.Mouth => MiiMaskLayers.Mouth,
            HeadPart.Mustache => MiiMaskLayers.Mustache,
            HeadPart.Mole => MiiMaskLayers.Mole,
            _ => MiiMaskLayers.None,
        };

    public static bool IsMaskPart(this HeadPart part) => part.MaskLayer() != MiiMaskLayers.None;

    /// <summary>The meshes that make up a part (for mask parts: the mask mesh they are painted on).</summary>
    public static bool Contains(this HeadPart part, HeadShape shape) =>
        part switch
        {
            HeadPart.Head => true,
            HeadPart.Faceline => shape == HeadShape.Faceline,
            HeadPart.Hair => shape is HeadShape.Hair or HeadShape.Cap or HeadShape.Forehead,
            HeadPart.Nose => shape is HeadShape.Nose or HeadShape.NoseLine,
            HeadPart.Beard => shape == HeadShape.Beard,
            HeadPart.Glasses => shape == HeadShape.Glass,
            _ => shape == HeadShape.Mask,
        };

    /// <summary>The meshes that light up when the part is highlighted (the skin under a hairstyle doesn't).</summary>
    public static bool Highlights(this HeadPart part, HeadShape shape) =>
        part == HeadPart.Hair ? shape is HeadShape.Hair or HeadShape.Cap : part.Contains(shape);
}
