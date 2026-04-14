using WheelWizard.GameBanana.Domain;
using WheelWizard.Resources.Languages;
using WheelWizard.Settings;

namespace WheelWizard.Services;

public enum ModStorageSystem
{
    MyStuff,
    Patches,
}

public static class ModStorageSystemHelper
{
    public static ModStorageSystem GetCurrent(ISettingsManager settingsManager)
    {
        return settingsManager.Get<bool>(settingsManager.USE_PATCHES_SYSTEM) ? ModStorageSystem.Patches : ModStorageSystem.MyStuff;
    }

    public static string GetDisplayName(ModStorageSystem storageSystem) =>
        storageSystem == ModStorageSystem.Patches ? "Patches" : Common.PageTitle_Mods;

    public static string GetClearFolderPrompt(ModStorageSystem storageSystem)
    {
        if (storageSystem == ModStorageSystem.MyStuff)
            return Phrases.Question_LaunchClearModsFound_Extra;

        //todo: translation lol
        return "You are about to launch the game without mods. Do you want to clear your Patches folder?";
    }

    public static string GetTargetFolderPath(ModStorageSystem storageSystem, bool isBeta = false)
    {
        if (storageSystem == ModStorageSystem.Patches)
            return isBeta ? PathManager.RrBetaPatchesFolderPath : PathManager.PatchesFolderPath;

        return isBeta ? PathManager.RrBetaMyStuffFolderPath : PathManager.MyStuffFolderPath;
    }

    public static string GetInactiveFolderPath(ModStorageSystem storageSystem, bool isBeta = false)
    {
        var inactiveStorageSystem = storageSystem == ModStorageSystem.Patches ? ModStorageSystem.MyStuff : ModStorageSystem.Patches;
        return GetTargetFolderPath(inactiveStorageSystem, isBeta);
    }

    public static bool UsesPatches(IEnumerable<GameBananaTag>? tags) => tags?.Any(tag => IsPatchesTag(tag.Title)) == true;

    public static bool IsPatchesTag(string? tagTitle)
    {
        var normalizedTitle = tagTitle?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
            return false;

        var titleOnly = normalizedTitle.Split(':', 2)[0].Trim();
        return normalizedTitle is not null
            && (
                titleOnly.Equals("patch", StringComparison.OrdinalIgnoreCase)
                || titleOnly.Equals("patches", StringComparison.OrdinalIgnoreCase)
            );
    }
}
