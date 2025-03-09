using WheelWizard.Helpers;
using WheelWizard.Models.Settings;

namespace WheelWizard.Services.Settings;

public class DolphinSettingManager
{
    private static string ConfigFolderPath(string fileName) => Path.Combine(PathManager.ConfigFolderPath, fileName);
    private bool _loaded;
    private readonly List<DolphinSetting> _settings = new();
    
    public static DolphinSettingManager Instance { get; } = new();
    private DolphinSettingManager() { }
    
    public void RegisterSetting(DolphinSetting setting)
    {
        if (_loaded) 
            return;
        
        _settings.Add(setting);
    }
    
    public void SaveSettings(DolphinSetting invokingSetting)
    {
        // TODO: This method definitely has to be optimized
        if (!_loaded) 
            return;
        
        foreach (var setting in _settings)
        {
            ChangeIniSettings(setting.FileName, setting.Section, setting.Name, setting.GetStringValue());
        }
    }
    
    public void ReloadSettings()
    {
        // TODO: this method could also be optimized by checking if the previously loaded directory
        //       is still the current ConfigFolderPath and if so, just not run the LoadSettings method again
        _loaded = false;
        LoadSettings();
    }
    
    public void LoadSettings()
    {
        if (_loaded) 
            return;
        
        if (!FileHelper.DirectoryExists(PathManager.ConfigFolderPath))
            return;
        
        // TODO: This method can maybe be optimized in the future, since now it reads the file for every setting
        //       and on top of that for reach setting it loops over each line and section and stuff like that.
        foreach (var setting in _settings)
        {
            var value = ReadIniSetting(setting.FileName, setting.Section, setting.Name);
            if (value == null)
                ChangeIniSettings(setting.FileName, setting.Section, setting.Name, setting.GetStringValue());
            else
                setting.SetFromString(value, true); // we read it, which means there is no purpose in saving it again
        }
        
        _loaded = true;
    }
    
    private static string[]? ReadIniFile(string fileName)
    {
        var filePath = ConfigFolderPath(fileName);
        var lines =  FileHelper.ReadAllLinesSafe(filePath);
        return lines;
    }
    
    private static string? ReadIniSetting(string fileName, string section, string settingToRead)
    {
        var lines = ReadIniFile(fileName);
        if (lines == null) 
            return null;
     
        var sectionIndex = Array.IndexOf(lines, $"[{section}]");
        if (sectionIndex == -1)
            return null;
        
        // find all the settings related to this section, we dont want to read/influence other sections
        var nextSectionName = lines.Skip(sectionIndex + 1)
                                   .FirstOrDefault(x => x.Trim().StartsWith("[") && x.Trim().EndsWith("]"));
        var nextSectionIndex = Array.IndexOf(lines, nextSectionName);
        var sectionLines = lines.Skip(sectionIndex + 1);
        if (nextSectionIndex != -1)
            sectionLines = sectionLines.Take(nextSectionIndex - sectionIndex - 1);
        
        // finally we can read the setting
        foreach (var line in sectionLines)
        {
            if (!line.StartsWith($"{settingToRead}=") && !line.StartsWith($"{settingToRead} ="))
                continue;
            //we found the setting, now we need to return the value
            var setting = line.Split("=");
            return setting[1].Trim();
        }

        return null;
    }
    
    // TODO: find out when to use `setting=value` and when to use `setting = value`
    private static void ChangeIniSettings(string fileName, string section, string settingToChange, string value)
    {
        var lines = ReadIniFile(fileName)?.ToList();
        if (lines == null)
            return;
        
        var sectionIndex = lines.IndexOf($"[{section}]");
        if (sectionIndex == -1)
        {
            lines.Add($"[{section}]");
            lines.Add($"{settingToChange} = {value}");
            FileHelper.WriteAllLines(ConfigFolderPath(fileName), lines);
            return;
        }

        for (var i = sectionIndex+1; i < lines.Count; i++)
        {
            //
            if (lines[i].Trim().StartsWith("[") && lines[i].Trim().EndsWith("]"))
                break; // Setting was not found in this section, so we have to append it to the section

            if (!lines[i].StartsWith($"{settingToChange}=") && !lines[i].StartsWith($"{settingToChange} ="))
                continue;
            
            lines[i] = $"{settingToChange} = {value}";
            FileHelper.WriteAllLines(ConfigFolderPath(fileName), lines);
            return;
        }
        // you only get here if the setting was not found in the section
        
        lines.Insert(sectionIndex + 1, $"{settingToChange} = {value}");
        FileHelper.WriteAllLines(ConfigFolderPath(fileName), lines);
    }
}
