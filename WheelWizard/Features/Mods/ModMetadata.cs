using IniParser.Model;
using IniParser.Parser;
using WheelWizard.Models.Mods;

namespace WheelWizard.Mods;

internal static class ModMetadata
{
    public static Mod Parse(string text)
    {
        var data = new IniDataParser().Parse(text);
        return new Mod
        {
            Title = data["Mod"]["Name"],
            Author = data["Mod"]["Author"],
            ModID = int.TryParse(data["Mod"]["ModID"], out var id) ? id : -1,
            IsEnabled = bool.TryParse(data["Mod"]["IsEnabled"], out var enabled) ? enabled : true,
            Priority = int.TryParse(data["Mod"]["Priority"], out var priority) ? priority : 0,
        };
    }

    public static string Serialize(Mod mod)
    {
        var data = new IniData();
        data["Mod"]["Name"] = mod.Title;
        data["Mod"]["Author"] = mod.Author;
        data["Mod"]["ModID"] = mod.ModID.ToString();
        data["Mod"]["IsEnabled"] = mod.IsEnabled.ToString();
        data["Mod"]["Priority"] = mod.Priority.ToString();
        return data.ToString();
    }
}
