namespace MiiAnim.Core.Animation;

/// <summary>
/// A named marker on a frame. Players fire it when playback reaches that frame, so an app can time
/// its own effects to the animation (e.g. swap the Mii's gender, play a sound, spawn dust).
/// </summary>
public readonly record struct AnimEvent(int Frame, string Name)
{
    public const int MaxNameLength = 64;
}
