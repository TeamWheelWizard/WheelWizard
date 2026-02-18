using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.Helpers;
using WheelWizard.Models.Settings;
using WheelWizard.Services;

namespace WheelWizard.Settings;

public class WhWzSettingManager(ILogger<WhWzSettingManager> logger) : IWhWzSettingManager
{
    private readonly object _syncRoot = new();
    private readonly object _fileIoSync = new();
    private bool _loaded;
    private readonly Dictionary<string, WhWzSetting> _settings = new();

    public void RegisterSetting(WhWzSetting setting)
    {
        lock (_syncRoot)
        {
            if (_loaded)
                return;

            _settings[setting.Name] = setting;
        }
    }

    public void SaveSettings(WhWzSetting invokingSetting)
    {
        Dictionary<string, WhWzSetting> settingsSnapshot;
        lock (_syncRoot)
        {
            if (!_loaded)
                return;

            settingsSnapshot = new(_settings);
        }

        var settingsToSave = new Dictionary<string, object?>();

        foreach (var (name, setting) in settingsSnapshot)
        {
            settingsToSave[name] = setting.Get();
        }

        var jsonString = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
        lock (_fileIoSync)
        {
            FileHelper.WriteAllTextSafe(PathManager.WheelWizardConfigFilePath, jsonString);
        }
    }

    public void LoadSettings()
    {
        Dictionary<string, WhWzSetting> settingsSnapshot;
        lock (_syncRoot)
        {
            if (_loaded)
                return;

            _loaded = true;
            settingsSnapshot = new(_settings);
        }

        // Even if it now returns early, loading has been considered complete.
        string? jsonString;
        lock (_fileIoSync)
        {
            jsonString = FileHelper.ReadAllTextSafe(PathManager.WheelWizardConfigFilePath);
        }

        if (jsonString == null)
            return;

        try
        {
            var loadedSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString);
            if (loadedSettings == null)
                return;

            foreach (var kvp in loadedSettings)
            {
                settingsSnapshot.TryGetValue(kvp.Key, out var setting);

                if (setting == null)
                    continue;

                setting.SetFromJson(kvp.Value, skipSave: true);
            }
        }
        catch (JsonException e)
        {
            logger.LogError(e, "Failed to deserialize the JSON config");
        }
    }
}
