using System.IO.Abstractions;
using WheelWizard.Services;
using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

/// <summary>
/// Reads and writes the recomp's <c>Config.toml</c> the same way <see cref="DolphinSettingManager"/>
/// handles Dolphin's ini files. The file has two writers — Wheel Wizard and the in-game settings
/// bar — so every write edits only its own key and preserves everything else: comments, ordering,
/// and every section Wheel Wizard does not own. The backend creates the file; when it does not
/// exist yet, the registered defaults (which mirror the runtime's own fallbacks) apply and nothing
/// is written.
/// </summary>
public class RecompSettingManager(IFileSystem fileSystem) : IRecompSettingManager
{
    private readonly object _syncRoot = new();
    private readonly object _fileIoSync = new();
    private bool _loaded;
    private readonly List<RecompSetting> _settings = [];

    public void RegisterSetting(RecompSetting setting)
    {
        lock (_syncRoot)
        {
            if (_loaded)
                return;

            _settings.Add(setting);
        }
    }

    public void SaveSettings(RecompSetting invokingSetting)
    {
        lock (_syncRoot)
        {
            if (!_loaded)
                return;
        }

        lock (_fileIoSync)
        {
            WriteTomlSetting(invokingSetting.Section, invokingSetting.Name, invokingSetting.GetStringValue());
        }
    }

    public void ReloadSettings()
    {
        lock (_syncRoot)
        {
            _loaded = false;
        }

        LoadSettings();
    }

    public void RemoveTomlSetting(string section, string settingToRemove)
    {
        lock (_fileIoSync)
        {
            var lines = ReadTomlFile()?.ToList();
            if (lines == null)
                return;

            var sectionHeader = $"[{section}]";
            var sectionIndex = lines.FindIndex(line => line.Trim() == sectionHeader);
            if (sectionIndex == -1)
                return;

            for (var i = sectionIndex + 1; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                    break; // Next section reached; the key is absent.

                if (!IsSettingLine(trimmed, settingToRemove))
                    continue;

                lines.RemoveAt(i);
                fileSystem.File.WriteAllLines(PathManager.RecompConfigFilePath, lines);
                return;
            }
        }
    }

    public void LoadSettings()
    {
        List<RecompSetting> settingsSnapshot;
        if (_loaded || !fileSystem.File.Exists(PathManager.RecompConfigFilePath))
            return;

        lock (_syncRoot)
        {
            if (_loaded)
                return;

            _loaded = true;
            settingsSnapshot = [.. _settings];
        }

        lock (_fileIoSync)
        {
            foreach (var setting in settingsSnapshot)
            {
                // A missing or unparsable key keeps the registered default without writing it back:
                // the runtime falls back to the very same default, so the file stays untouched until
                // the user actually changes something.
                var value = ReadTomlSetting(setting.Section, setting.Name);
                if (value != null)
                    setting.SetFromString(value, true);
            }
        }
    }

    private string[]? ReadTomlFile()
    {
        if (!fileSystem.File.Exists(PathManager.RecompConfigFilePath))
            return null;

        try
        {
            return fileSystem.File.ReadAllLines(PathManager.RecompConfigFilePath);
        }
        catch
        {
            return null;
        }
    }

    private string? ReadTomlSetting(string section, string settingToRead)
    {
        var lines = ReadTomlFile();
        if (lines == null)
            return null;

        var inSection = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inSection = trimmed == $"[{section}]";
                continue;
            }

            if (!inSection || !IsSettingLine(trimmed, settingToRead))
                continue;

            var value = trimmed[(trimmed.IndexOf('=') + 1)..].Trim();

            // The runtime writes comments only on their own lines, but tolerate a trailing one on an
            // unquoted value rather than reading it as part of the value.
            if (!value.StartsWith('"') && !value.StartsWith('\'') && value.Contains('#'))
                value = value[..value.IndexOf('#')].Trim();
            return value;
        }

        return null;
    }

    private void WriteTomlSetting(string section, string settingToChange, string value)
    {
        var lines = ReadTomlFile()?.ToList();

        // The backend owns creating Config.toml; a write before it exists would hand the runtime a
        // file Wheel Wizard invented, so the value simply stays in memory until the next load.
        if (lines == null)
            return;

        var sectionIndex = lines.FindIndex(line => line.Trim() == $"[{section}]");
        if (sectionIndex == -1)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines.Add(string.Empty);
            lines.Add($"[{section}]");
            lines.Add($"{settingToChange} = {value}");
            fileSystem.File.WriteAllLines(PathManager.RecompConfigFilePath, lines);
            return;
        }

        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                break; // Next section reached; the key is absent and gets inserted below.

            if (!IsSettingLine(trimmed, settingToChange))
                continue;

            lines[i] = $"{settingToChange} = {value}";
            fileSystem.File.WriteAllLines(PathManager.RecompConfigFilePath, lines);
            return;
        }

        lines.Insert(sectionIndex + 1, $"{settingToChange} = {value}");
        fileSystem.File.WriteAllLines(PathManager.RecompConfigFilePath, lines);
    }

    private static bool IsSettingLine(string trimmedLine, string settingName) =>
        trimmedLine.StartsWith($"{settingName}=") || trimmedLine.StartsWith($"{settingName} =");
}
