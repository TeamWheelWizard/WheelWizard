namespace WheelWizard.MiiAnimations.Editor;

/// <summary>Something that happened in the Mii editor that the Mii reacts to.</summary>
public enum MiiEditorReaction
{
    /// <summary>The editor opened.</summary>
    Enter,
    BodyShape,
    Name,
    FavoriteColor,
    Favorite,
    Unfavorite,

    /// <summary>A new random Mii. Show it at the <see cref="MiiEditorCues.Randomize"/> cue.</summary>
    Randomize,

    /// <summary>The Mii was saved. Close the editor at the <see cref="MiiEditorCues.Saved"/> cue.</summary>
    Save,

    /// <summary>Show the girl at the <see cref="MiiEditorCues.SwapGender"/> cue.</summary>
    BecomeGirl,

    /// <summary>Show the boy at the <see cref="MiiEditorCues.SwapGender"/> cue.</summary>
    BecomeBoy,
    Face,
    Hair,
    Eyebrows,
    Eyes,
    Nose,
    Mouth,
    Glasses,
    FacialHair,
    Mole,
}

/// <summary>Event markers in the editor animations that the editor acts on.</summary>
public static class MiiEditorCues
{
    public const string SwapGender = "swap_gender";
    public const string Randomize = "randomize";
    public const string Saved = "saved";
}

/// <summary>What the editor camera looks at.</summary>
public enum MiiEditorFocus
{
    /// <summary>The whole Mii.</summary>
    Body,

    /// <summary>Head and shoulders, for the face menus. Only animations that keep the head in place play here.</summary>
    Face,
}

/// <summary>Where the Mii was clicked.</summary>
public enum MiiBodyPart
{
    Head,
    Body,
    LeftLeg,
    RightLeg,
}
