using System.IO.Abstractions;
using WheelWizard.Services;
using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

public class DolphinSettingManager(IFileSystem fileSystem) : IDolphinSettingManager
{
    private string ConfigFolderPath(string fileName) => fileSystem.Path.Combine(PathManager.ConfigFolderPath, fileName);

    // LOCKS:
    // We use locks to keep the settings state and file IO consistent.
    // Even though we do not manually create threads in this class, work can still happen concurrently
    // (for example the Avalonia UI thread + Task/thread-pool execution), so synchronization is still required.

    // Sync Root:  Responsible for synchronizing access to the _settings list and the _loaded flag.
    // It ensures that multiple threads don't modify the settings list or the loaded state at the same time
    // File IO Sync:  Responsible for reading and writing the INI files. It ensures that multiple threads don't read/write at the same time
    private readonly object _syncRoot = new();
    private readonly object _fileIoSync = new();
    private bool _loaded;
    private readonly List<DolphinSetting> _settings = [];

    public void RegisterSetting(DolphinSetting setting)
    {
        lock (_syncRoot)
        {
            if (_loaded)
                return;

            _settings.Add(setting);
        }
    }

    public void SaveSettings(DolphinSetting invokingSetting)
    {
        List<DolphinSetting> settingsSnapshot;
        lock (_syncRoot)
        {
            // TODO: This method definitely has to be optimized
            if (!_loaded)
                return;

            settingsSnapshot = [.. _settings];
        }

        lock (_fileIoSync)
        {
            foreach (var setting in settingsSnapshot)
            {
                ChangeIniSettings(setting.FileName, setting.Section, setting.Name, setting.GetStringValue());
            }
        }
    }

    public void ReloadSettings()
    {
        lock (_syncRoot)
        {
            // TODO: this method could also be optimized by checking if the previously loaded directory
            //       is still the current ConfigFolderPath and if so, just not run the LoadSettings method again
            _loaded = false;
        }

        LoadSettings();
    }

    public void LoadSettings()
    {
        List<DolphinSetting> settingsSnapshot;
        if (_loaded || !fileSystem.Directory.Exists(PathManager.ConfigFolderPath))
            return;

        lock (_syncRoot)
        {
            // Since we are working with concurrency here, we have to check loaded again since it might be changed while we were waiting
            // for the lock to open
            if (_loaded)
                return;

            _loaded = true;
            settingsSnapshot = [.. _settings];
        }

        // TODO: This method can maybe be optimized in the future, since now it reads the file for every setting
        //       and on top of that for reach setting it loops over each line and section and stuff like that.
        lock (_fileIoSync)
        {
            foreach (var setting in settingsSnapshot)
            {
                var value = ReadIniSetting(setting.FileName, setting.Section, setting.Name);
                if (value == null)
                    ChangeIniSettings(setting.FileName, setting.Section, setting.Name, setting.GetStringValue());
                else
                    setting.SetFromString(value, true); // we read it, which means there is no purpose in saving it again
            }
        }
    }

    private string[]? ReadIniFile(string fileName)
    {
        var filePath = ConfigFolderPath(fileName);
        if (!fileSystem.File.Exists(filePath))
            return null;

        try
        {
            return fileSystem.File.ReadAllLines(filePath);
        }
        catch
        {
            return null;
        }
    }

    private string? ReadIniSetting(string fileName, string section, string settingToRead)
    {
        var lines = ReadIniFile(fileName);
        if (lines == null)
            return null;

        var sectionIndex = Array.IndexOf(lines, $"[{section}]");
        if (sectionIndex == -1)
            return null;

        // find all the settings related to this section, we dont want to read/influence other sections
        var nextSectionName = lines.Skip(sectionIndex + 1).FirstOrDefault(x => x.Trim().StartsWith("[") && x.Trim().EndsWith("]"));
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
    private void ChangeIniSettings(string fileName, string section, string settingToChange, string value)
    {
        var lines = ReadIniFile(fileName)?.ToList();
        if (lines == null)
            return;

        var sectionIndex = lines.IndexOf($"[{section}]");
        if (sectionIndex == -1)
        {
            lines.Add($"[{section}]");
            lines.Add($"{settingToChange} = {value}");
            fileSystem.File.WriteAllLines(ConfigFolderPath(fileName), lines);
            return;
        }

        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            //
            if (lines[i].Trim().StartsWith("[") && lines[i].Trim().EndsWith("]"))
                break; // Setting was not found in this section, so we have to append it to the section

            if (!lines[i].StartsWith($"{settingToChange}=") && !lines[i].StartsWith($"{settingToChange} ="))
                continue;

            lines[i] = $"{settingToChange} = {value}";
            fileSystem.File.WriteAllLines(ConfigFolderPath(fileName), lines);
            return;
        }
        // you only get here if the setting was not found in the section

        lines.Insert(sectionIndex + 1, $"{settingToChange} = {value}");
        fileSystem.File.WriteAllLines(ConfigFolderPath(fileName), lines);
    }
}
