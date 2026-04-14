using System.Text;
using System.Xml.Linq;
using WheelWizard.Models.Enums;
using WheelWizard.Services.Other;

namespace WheelWizard.Services.Launcher.Helpers;

public static class RetroRewindAspectRatioHelper
{
    private const string AspectOptionName = "Aspect Ratio";

    private sealed record AspectPatchIds(string Ratio16By9, string Ratio21By9, string Ratio32By9, string OpenMatte16By10);

    private static RrAspectRatioMode CurrentMode => (RrAspectRatioMode)SettingsManager.RR_ASPECT_RATIO.Get();

    public static int GetSelectedChoice() => (int)CurrentMode;

    public static void ApplyDolphinGraphicsAspectRatio()
    {
        var gfxAspectRatio = CurrentMode switch
        {
            RrAspectRatioMode.Widescreen16By9 => 1,
            RrAspectRatioMode.OpenMatte16By10 => 2,
            _ => 3,
        };

        SettingsManager.GFX_ASPECT_RATIO.Set(gfxAspectRatio);
    }

    public static string GetWiiAspectLaunchConfig()
    {
        var useWidescreen = CurrentMode != RrAspectRatioMode.OpenMatte16By10;
        return $"--config=SYSCONF.IPL.AR={(useWidescreen ? "True" : "False")}";
    }

    public static void EnsureAspectPatches(string xmlFilePath, string sectionName)
    {
        if (!File.Exists(xmlFilePath))
            return;

        var xml = XDocument.Load(xmlFilePath, LoadOptions.PreserveWhitespace);
        if (xml.Root == null)
            return;

        var options = xml.Root.Element("options");
        var section = options?.Elements("section").FirstOrDefault(element => AttrValueEquals(element, "name", sectionName));
        if (section == null)
            return;

        var fileName = Path.GetFileName(xmlFilePath);
        var isBeta = fileName.Equals("RRBeta.xml", StringComparison.OrdinalIgnoreCase);
        var patchPrefix = isBeta ? "RRBeta" : "RR";
        var patchIds = new AspectPatchIds(
            $"{patchPrefix}Aspect16By9",
            $"{patchPrefix}Aspect21By9",
            $"{patchPrefix}Aspect32By9",
            $"{patchPrefix}AspectOpenMatte16By10"
        );

        var launchRegion = DetectLaunchRegion();
        var changed = UpsertAspectOption(section, patchIds);
        changed |= UpsertAspectPatches(xml.Root, patchIds, CurrentMode, launchRegion);

        if (changed)
            xml.Save(xmlFilePath);
    }

    private static bool UpsertAspectOption(XElement section, AspectPatchIds patchIds)
    {
        var newOption = BuildAspectOption(patchIds);
        var existing = section.Elements("option").FirstOrDefault(element => AttrValueEquals(element, "name", AspectOptionName));
        if (existing != null && XmlEquals(existing, newOption))
            return false;

        if (existing != null)
        {
            existing.ReplaceWith(newOption);
            return true;
        }

        var saveOption = section.Elements("option").FirstOrDefault(element => AttrValueEquals(element, "name", "Seperate Savegame"));
        if (saveOption != null)
            saveOption.AddAfterSelf(newOption);
        else
            section.Add(newOption);

        return true;
    }

    private static bool UpsertAspectPatches(
        XElement root,
        AspectPatchIds patchIds,
        RrAspectRatioMode selectedMode,
        MarioKartWiiEnums.Regions launchRegion
    )
    {
        var changed = false;

        changed |= UpsertPatch(root, BuildModePatch(patchIds.Ratio16By9, RrAspectRatioMode.Widescreen16By9, selectedMode, launchRegion));
        changed |= UpsertPatch(root, BuildModePatch(patchIds.Ratio21By9, RrAspectRatioMode.UltraWide21By9, selectedMode, launchRegion));
        changed |= UpsertPatch(
            root,
            BuildModePatch(patchIds.Ratio32By9, RrAspectRatioMode.SuperUltraWide32By9, selectedMode, launchRegion)
        );
        changed |= UpsertPatch(
            root,
            BuildModePatch(patchIds.OpenMatte16By10, RrAspectRatioMode.OpenMatte16By10, selectedMode, launchRegion)
        );

        return changed;
    }

    private static bool UpsertPatch(XElement root, XElement patch)
    {
        var patchId = patch.Attribute("id")?.Value;
        var existing = root.Elements("patch").FirstOrDefault(element => AttrValueEquals(element, "id", patchId));
        if (existing != null && XmlEquals(existing, patch))
            return false;

        if (existing != null)
            existing.ReplaceWith(patch);
        else
            root.Add(patch);

        return true;
    }

    private static XElement BuildAspectOption(AspectPatchIds patchIds)
    {
        return new XElement(
            "option",
            new XAttribute("name", AspectOptionName),
            new XElement("choice", new XAttribute("name", "16:9"), new XElement("patch", new XAttribute("id", patchIds.Ratio16By9))),
            new XElement("choice", new XAttribute("name", "21:9"), new XElement("patch", new XAttribute("id", patchIds.Ratio21By9))),
            new XElement("choice", new XAttribute("name", "32:9"), new XElement("patch", new XAttribute("id", patchIds.Ratio32By9))),
            new XElement(
                "choice",
                new XAttribute("name", "16:10 (4:3 Open Matte)"),
                new XElement("patch", new XAttribute("id", patchIds.OpenMatte16By10))
            )
        );
    }

    private static XElement BuildModePatch(
        string patchId,
        RrAspectRatioMode modeForPatch,
        RrAspectRatioMode selectedMode,
        MarioKartWiiEnums.Regions launchRegion
    )
    {
        var patch = new XElement("patch", new XAttribute("id", patchId));
        if (modeForPatch != selectedMode)
            return patch;

        var writes = GetWritesForModeAndRegion(modeForPatch, launchRegion);
        foreach (var (offset, value) in writes)
        {
            patch.Add(MemoryWrite(offset, value));
        }

        return patch;
    }

    private static IReadOnlyList<(string Offset, string Value)> GetWritesForModeAndRegion(
        RrAspectRatioMode mode,
        MarioKartWiiEnums.Regions region
    )
    {
        return mode switch
        {
            RrAspectRatioMode.Widescreen16By9 => [],
            RrAspectRatioMode.UltraWide21By9 => Get21By9Writes(region),
            RrAspectRatioMode.SuperUltraWide32By9 => Get32By9Writes(region),
            RrAspectRatioMode.OpenMatte16By10 => Get16By10OpenMatteWrites(region),
            _ => [],
        };
    }

    private static IReadOnlyList<(string Offset, string Value)> Get21By9Writes(MarioKartWiiEnums.Regions region)
    {
        return region switch
        {
            MarioKartWiiEnums.Regions.Europe =>
            [
                ("0x802A3EF4", "0454"),
                ("0x802A3EF8", "3F0C79FE"),
                ("0x802A3EE8", "032C"),
                ("0x802A3EEC", "3F3FAF4A"),
            ],
            MarioKartWiiEnums.Regions.America =>
            [
                ("0x8029FB8C", "0454"),
                ("0x8029FB90", "3F0C79FE"),
                ("0x8029FB80", "032C"),
                ("0x8029FB84", "3F3FAF4A"),
            ],
            MarioKartWiiEnums.Regions.Japan =>
            [
                ("0x802A3894", "0454"),
                ("0x802A3898", "3F0C79FE"),
                ("0x802A3888", "032C"),
                ("0x802A388C", "3F3FAF4A"),
            ],
            _ => [],
        };
    }

    private static IReadOnlyList<(string Offset, string Value)> Get32By9Writes(MarioKartWiiEnums.Regions region)
    {
        return region switch
        {
            MarioKartWiiEnums.Regions.Europe =>
            [
                ("0x802A3EF4", "0680"),
                ("0x802A3EF8", "3EBB13B1"),
                ("0x802A3EE8", "04C0"),
                ("0x802A3EEC", "3F000000"),
            ],
            MarioKartWiiEnums.Regions.America =>
            [
                ("0x8029FB8C", "0680"),
                ("0x8029FB90", "3EBB13B1"),
                ("0x8029FB80", "04C0"),
                ("0x8029FB84", "3F000000"),
            ],
            MarioKartWiiEnums.Regions.Japan =>
            [
                ("0x802A3894", "0680"),
                ("0x802A3898", "3EBB13B1"),
                ("0x802A3888", "04C0"),
                ("0x802A388C", "3F000000"),
            ],
            _ => [],
        };
    }

    private static IReadOnlyList<(string Offset, string Value)> Get16By10OpenMatteWrites(MarioKartWiiEnums.Regions region)
    {
        return region switch
        {
            MarioKartWiiEnums.Regions.Europe => [("0x802A3EE8", "02DC"), ("0x802A3EEC", "3F54A246")],
            MarioKartWiiEnums.Regions.America => [("0x8029FB80", "02DC"), ("0x8029FB84", "3F54A246")],
            MarioKartWiiEnums.Regions.Japan =>
            [
                ("0x802A3EF4", "02ED"),
                ("0x802A3EF8", "3F4FCEC8"),
                ("0x802A3EE8", "02DA"),
                ("0x802A3EEC", "3F553769"),
            ],
            _ => [],
        };
    }

    private static MarioKartWiiEnums.Regions DetectLaunchRegion()
    {
        var regionFromGame = TryGetRegionFromGameFile(PathManager.GameFilePath);
        if (regionFromGame != MarioKartWiiEnums.Regions.None)
            return regionFromGame;

        var configuredRegion = (MarioKartWiiEnums.Regions)SettingsManager.RR_REGION.Get();
        if (configuredRegion != MarioKartWiiEnums.Regions.None)
            return configuredRegion;

        return RRRegionManager.GetValidRegions().FirstOrDefault();
    }

    private static MarioKartWiiEnums.Regions TryGetRegionFromGameFile(string gameFilePath)
    {
        if (string.IsNullOrWhiteSpace(gameFilePath) || !File.Exists(gameFilePath))
            return MarioKartWiiEnums.Regions.None;

        try
        {
            using var stream = new FileStream(gameFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var region = TryReadRegionFromGameIdAt(stream, 0x0);
            if (region != MarioKartWiiEnums.Regions.None)
                return region;

            // WBFS files usually start the game header at 0x200.
            return TryReadRegionFromGameIdAt(stream, 0x200);
        }
        catch
        {
            return MarioKartWiiEnums.Regions.None;
        }
    }

    private static MarioKartWiiEnums.Regions TryReadRegionFromGameIdAt(Stream stream, long offset)
    {
        if (stream.Length < offset + 4)
            return MarioKartWiiEnums.Regions.None;

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[6];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);
        if (bytesRead < 4)
            return MarioKartWiiEnums.Regions.None;

        var gameId = Encoding.ASCII.GetString(buffer, 0, bytesRead).TrimEnd('\0', ' ');
        return ParseRegionFromGameId(gameId);
    }

    private static MarioKartWiiEnums.Regions ParseRegionFromGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return MarioKartWiiEnums.Regions.None;

        var normalized = gameId.ToUpperInvariant();
        if (normalized.Length < 4 || !normalized.StartsWith("RMC", StringComparison.Ordinal))
            return MarioKartWiiEnums.Regions.None;

        return normalized[3] switch
        {
            'E' => MarioKartWiiEnums.Regions.America,
            'P' => MarioKartWiiEnums.Regions.Europe,
            'J' => MarioKartWiiEnums.Regions.Japan,
            'K' => MarioKartWiiEnums.Regions.Korea,
            _ => MarioKartWiiEnums.Regions.None,
        };
    }

    private static XElement MemoryWrite(string offset, string value)
    {
        return new XElement("memory", new XAttribute("offset", offset), new XAttribute("value", value));
    }

    private static bool AttrValueEquals(XElement element, string attributeName, string? expectedValue)
    {
        return string.Equals(element.Attribute(attributeName)?.Value, expectedValue, StringComparison.Ordinal);
    }

    private static bool XmlEquals(XElement first, XElement second)
    {
        return first.ToString(SaveOptions.DisableFormatting) == second.ToString(SaveOptions.DisableFormatting);
    }
}
