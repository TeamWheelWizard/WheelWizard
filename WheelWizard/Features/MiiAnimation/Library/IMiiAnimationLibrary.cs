using MiiAnim.Core.Animation;

namespace WheelWizard.MiiAnimations.Library;

/// <summary>
/// The animations that ship with WheelWizard (Resources/Animations), addressed by their path
/// without extension, e.g. "editor/idle/Editor_idle_calm_breathing". Returned animations are shared: don't edit them.
/// </summary>
public interface IMiiAnimationLibrary
{
    /// <summary>The animation at <paramref name="path"/>, or null when it doesn't exist or can't be read.</summary>
    MiiAnimation? Get(string path);

    /// <summary>Paths of the animations in <paramref name="folder"/> (not its subfolders), sorted.</summary>
    IReadOnlyList<string> List(string folder);
}
