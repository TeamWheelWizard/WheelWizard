namespace MiiAnim.Core.Animation;

/// <summary>
/// An extra Mii in an animation, next to the main one (actor 0, the Mii the app is showing).
/// The app decides which Mii plays it by <see cref="Name"/>; e.g. WheelWizard's "change Mii" animation has an actor
/// named "old" for the previous Mii. Without one, players show <see cref="PreviewMii"/>.
/// <para>
/// <see cref="Motion"/> holds this Mii's tracks. It shares the timing, events, actors and particles of the animation
/// it belongs to, so anything that plays or edits a <see cref="MiiAnimation"/> works on it unchanged.
/// </para>
/// </summary>
public sealed class AnimActor
{
    public const int MaxNameLength = 32;

    internal AnimActor(string name, MiiAnimation motion)
    {
        Name = name;
        Motion = motion;
    }

    /// <summary>Role name the app uses to pick the Mii (e.g. "old").</summary>
    public string Name { get; set; }

    public MiiAnimation Motion { get; }

    /// <summary>74-byte Wii Mii shown when the app has no Mii for this role (stored as the motion's author Mii).</summary>
    public byte[]? PreviewMii
    {
        get => Motion.AuthorMii;
        set => Motion.AuthorMii = value;
    }
}
