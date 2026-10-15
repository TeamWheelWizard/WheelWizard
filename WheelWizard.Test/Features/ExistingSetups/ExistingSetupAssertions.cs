using System.Text.Json;
using WheelWizard.CustomDistributions;

namespace WheelWizard.Test.Features.ExistingSetups;

internal static class ExistingSetupAssertions
{
    /// <summary>
    /// What an existing user relies on after an update: they are not onboarded again, the settings they
    /// picked still mean the same, Dolphin's own files are untouched, and Retro Rewind (with its save) is
    /// where Wheel Wizard now looks for it.
    /// </summary>
    public static void AssertStillWorks(
        Session session,
        Dictionary<string, string>? retroRewind = null,
        string? expectedRoot = null,
        bool expectPlayable = true
    )
    {
        var install = session.Install;

        Assert.False(session.Onboarding.IsRequired, "an existing setup must never be sent through the onboarding");
        AssertUserSettingsUntouched(session);
        Assert.False(session.ManagedDolphin.IsActive, "an existing Dolphin must not be turned into a managed one");

        if (expectPlayable)
            Assert.True(session.Settings.PathsSetupCorrectly(), "the paths that were valid before are still valid");

        if (expectedRoot is not null)
            Assert.Equal(expectedRoot, session.Paths.RootFolderPath);

        if (retroRewind is not null)
        {
            var current = install.Snapshot(session.Paths.RootFolderPath);
            current.Remove(CustomDistributionPaths.MigratedMarkerFileName);
            Assert.Equal(retroRewind.OrderBy(pair => pair.Key), current.OrderBy(pair => pair.Key));
            Assert.Equal(ExistingInstall.RetroRewindVersion, session.InstalledRetroRewindVersion());
            Assert.Equal(
                retroRewind["riivolution/save/RetroWFC/RMCP/rksys.dat"],
                install.Fs.File.ReadAllText(install.Combine(session.Paths.SaveFolderPath, "RMCP", "rksys.dat"))
            );
        }

        if (expectPlayable)
            AssertLaunchPointsAtRetroRewind(session);
        AssertNoLeftovers(session);
    }

    public static void AssertUserSettingsUntouched(Session session)
    {
        var install = session.Install;
        Assert.Equal(install.DolphinLocation, session.Settings.DOLPHIN_LOCATION.Get());
        Assert.Equal(install.UserFolder, session.Settings.USER_FOLDER_PATH.Get());
        Assert.Equal(install.GameFile, session.Settings.GAME_LOCATION.Get());
        if (session.InitialDolphinIni is not null)
            Assert.Equal(session.InitialDolphinIni, install.Fs.File.ReadAllText(install.DolphinIniPath));
    }

    public static void AssertLaunchPointsAtRetroRewind(Session session)
    {
        var install = session.Install;
        var root = session.ReadLaunchRoot();
        Assert.Equal(install.Fs.Path.GetFullPath(session.Paths.RootFolderPath), root);
        var xml = session.ReadLaunchXml();
        Assert.True(install.Fs.File.Exists(xml), $"the Riivolution XML {xml} Dolphin is launched with must exist");
        Assert.StartsWith(root, xml, StringComparison.OrdinalIgnoreCase);
        Assert.True(install.Fs.Directory.Exists(root));
    }

    public static void AssertNoLeftovers(Session session)
    {
        var install = session.Install;
        var parent = install.Fs.Path.GetDirectoryName(install.ManagedRoot)!;
        var siblings = install.Fs.Directory.GetDirectories(parent).Select(install.Fs.Path.GetFileName).ToArray();
        Assert.DoesNotContain(siblings, name => name!.EndsWith(".migrating", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Saving rewrites config.json with every registered setting, so new keys with defaults show up; what
    /// matters is that nothing the user had, including keys this version does not know, changed its value.
    /// </summary>
    public static void AssertConfigKeepsWhatTheUserHad(Session session)
    {
        var install = session.Install;
        Assert.NotNull(session.InitialConfigJson);
        var before = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(session.InitialConfigJson!)!;
        var after = install.ReadConfig();
        foreach (var (key, value) in before)
        {
            Assert.True(after.ContainsKey(key), $"config.json lost {key}");
            Assert.Equal(value.GetRawText(), after[key].GetRawText());
        }
    }
}
