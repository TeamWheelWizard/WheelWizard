using System.IO.Abstractions;
using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

public class DolphinSettingManager(IFileSystem fileSystem) : IDolphinSettingManager
{
    private readonly object _sync = new();
    private bool _loaded;
    private readonly List<IDolphinSetting> _settings = [];

    public void RegisterSetting(IDolphinSetting setting)
    {
        lock (_sync)
        {
            if (!_loaded)
                _settings.Add(setting);
        }
    }

    public void SaveSettings(string configDirectory, IDolphinSetting invokingSetting)
    {
        lock (_sync)
        {
            if (!_loaded || !fileSystem.Directory.Exists(configDirectory))
                throw new IOException("The Dolphin configuration directory is not available.");

            var path = fileSystem.Path.Combine(configDirectory, invokingSetting.FileName);
            var lines = fileSystem.File.Exists(path) ? fileSystem.File.ReadAllLines(path).ToList() : [];
            var header = $"[{invokingSetting.Section}]";
            var section = lines.FindIndex(line => line.Trim() == header);
            var replacement = $"{invokingSetting.Name} = {invokingSetting.GetStringValue()}";
            if (section < 0)
            {
                lines.Add(header);
                lines.Add(replacement);
            }
            else
            {
                var index = section + 1;
                for (; index < lines.Count && !IsSection(lines[index]); index++)
                {
                    if (Key(lines[index]) != invokingSetting.Name)
                        continue;
                    lines[index] = replacement;
                    SettingsFile.WriteLines(fileSystem, path, lines);
                    return;
                }
                lines.Insert(index, replacement);
            }
            SettingsFile.WriteLines(fileSystem, path, lines);
        }
    }

    public void ReloadSettings(string configDirectory)
    {
        lock (_sync)
        {
            _loaded = false;
            LoadSettings(configDirectory);
        }
    }

    public void LoadSettings(string configDirectory)
    {
        lock (_sync)
        {
            if (_loaded)
                return;

            // Read each file once; loading never writes defaults into another application's config.
            foreach (var group in _settings.GroupBy(setting => setting.FileName))
            {
                var path = fileSystem.Path.Combine(configDirectory, group.Key);
                string[] lines;
                try
                {
                    lines = fileSystem.File.Exists(path) ? fileSystem.File.ReadAllLines(path) : [];
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    lines = [];
                }
                foreach (var setting in group)
                {
                    var value = ReadValue(lines, setting.Section, setting.Name);
                    if (value == null || !setting.SetFromString(value, skipSave: true))
                        setting.Reset(skipSave: true);
                }
            }
            _loaded = true;
        }
    }

    private static bool IsSection(string line) => line.Trim() is var text && text.StartsWith('[') && text.EndsWith(']');

    private static string? Key(string line) => line.IndexOf('=') is var index && index >= 0 ? line[..index].Trim() : null;

    private static string? ReadValue(IEnumerable<string> lines, string section, string key)
    {
        var inSection = false;
        foreach (var line in lines)
        {
            if (IsSection(line))
                inSection = line.Trim() == $"[{section}]";
            else if (inSection && Key(line) == key)
                return line[(line.IndexOf('=') + 1)..].Trim();
        }
        return null;
    }
}
