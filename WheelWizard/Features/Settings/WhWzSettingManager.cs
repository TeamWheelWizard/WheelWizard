using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.Services;
using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

public class WhWzSettingManager(ILogger<WhWzSettingManager> logger, IFileSystem fileSystem) : IWhWzSettingManager
{
    // LOCKS:
    // We are working with locks. This is to ensure that we always have accurate information in our settings / application.
    // We do not create multiple threads. However, some of our features run through Tasks. Those are executed asynchronously, therefore still require locks.

    // Sync Root:  Responsible for synchronizing access to the _settings list and the _loaded flag.
    // It ensures that multiple threads don't modify the settings list or the loaded state at the same time
    // File IO Sync:  Responsible for reading and writing the INI files. It ensures that multiple threads don't read/write at the same time
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
            var configPath = PathManager.WheelWizardConfigFilePath;
            try
            {
                var directoryPath = fileSystem.Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(directoryPath) && !fileSystem.Directory.Exists(directoryPath))
                    fileSystem.Directory.CreateDirectory(directoryPath);

                fileSystem.File.WriteAllText(configPath, jsonString);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save settings file: {Path}", configPath);
            }
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
            var configPath = PathManager.WheelWizardConfigFilePath;
            try
            {
                jsonString = fileSystem.File.Exists(configPath) ? fileSystem.File.ReadAllText(configPath) : null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read settings file: {Path}", configPath);
                jsonString = null;
            }
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
                if (!settingsSnapshot.TryGetValue(kvp.Key, out var setting))
                    continue;

                try
                {
                    var success = setting.SetFromJson(kvp.Value, skipSave: true);
                    if (!success)
                        setting.Set(setting.DefaultValue, skipSave: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Invalid value for setting {SettingName}; resetting to default.", setting.Name);
                    setting.Set(setting.DefaultValue, skipSave: true);
                }
            }
        }
        catch (JsonException e)
        {
            logger.LogError(e, "Failed to deserialize the JSON config");
        }
    }
}
