using MiiAnim.Core.Animation;
using Testably.Abstractions.RandomSystem;

namespace WheelWizard.MiiAnimations.Library;

/// <summary>
/// Picks random clips from library folders, so adding a variant is just adding a file. Never picks the same clip
/// twice in a row from the same pool (unless it's the only one).
/// </summary>
public sealed class MiiClipPicker(IMiiAnimationLibrary library, IRandom random)
{
    private readonly Dictionary<string, string> _lastPicked = new();

    /// <summary>A random clip from <paramref name="folder"/> (not its subfolders), or null when it has none.</summary>
    public MiiAnimation? Pick(string folder) => Pick([folder]);

    /// <summary>A random clip from all of <paramref name="folders"/> together.</summary>
    public MiiAnimation? Pick(IReadOnlyList<string> folders)
    {
        var key = string.Join('|', folders);
        var pool = folders.SelectMany(library.List).Distinct().ToList();
        _lastPicked.TryGetValue(key, out var last);
        var choices = pool.Where(path => path != last || pool.Count == 1).ToList();
        while (choices.Count > 0)
        {
            var path = choices[random.Next(choices.Count)];
            if (library.Get(path) is { } clip)
            {
                _lastPicked[key] = path;
                return clip;
            }
            choices.Remove(path);
        }

        return null;
    }
}
