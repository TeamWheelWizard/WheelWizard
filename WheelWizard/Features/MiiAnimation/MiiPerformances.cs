using WheelWizard.MiiAnimations.Playback;

namespace WheelWizard.MiiAnimations;

/// <summary>What the Miis around the app do (folders of Features/MiiAnimation/Resources/Animations).</summary>
public static class MiiPerformances
{
    /// <summary>A friends card of a friend who's online: awake, and waving at you now and then.</summary>
    public static readonly MiiPerformance FriendOnline = new("friends/online")
    {
        Extras = "friends/online/wave",
        ExtrasAfter = TimeSpan.FromSeconds(6),
        ExtrasBefore = TimeSpan.FromSeconds(24),
        Arrival = "friends/wake_up",
    };

    /// <summary>A friends card of a friend who's offline: fast asleep.</summary>
    public static readonly MiiPerformance FriendOffline = new("friends/asleep") { Arrival = "friends/doze_off" };

    /// <summary>The player card in the sidebar (framed like the friends cards): just idling.</summary>
    public static readonly MiiPerformance SidebarPlayer = new("friends/online");

    /// <summary>The license card on the profile page, which the Mii stands behind like a window.</summary>
    public static readonly MiiPerformance ProfileWindow = new("profile/window/idle", "profile/idle");
}
