using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

public class WhWzSettingManager(ILogger<WhWzSettingManager> logger, IFileSystem fileSystem) : IWhWzSettingManager
{
    private readonly object _sync = new();
    private bool _loaded;
    private Exception? _loadError;
    private readonly Dictionary<string, WhWzSetting> _settings = new();
    private readonly Dictionary<string, JsonElement> _unknownSettings = new();

    public void RegisterSetting(WhWzSetting setting)
    {
        lock (_sync)
        {
            if (!_loaded)
                _settings[setting.Name] = setting;
        }
    }

    public void SaveSettings(string configPath, WhWzSetting invokingSetting)
    {
        lock (_sync)
        {
            if (!_loaded)
                throw new IOException("Application settings have not been loaded.");
            if (_loadError != null)
                throw new IOException("The settings file could not be loaded; the original file has been preserved.", _loadError);

            var values = _unknownSettings.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
            foreach (var (name, setting) in _settings)
                values[name] = setting.Get();

            try
            {
                SettingsFile.Write(
                    fileSystem,
                    configPath,
                    JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true })
                );
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to save settings file: {Path}", configPath);
                throw;
            }
        }
    }

    public void LoadSettings(string configPath)
    {
        lock (_sync)
        {
            if (_loaded)
                return;
            try
            {
                if (!fileSystem.File.Exists(configPath))
                    return;
                var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fileSystem.File.ReadAllText(configPath));
                if (values == null)
                    throw new JsonException("Expected a settings object.");
                foreach (var (name, value) in values)
                {
                    if (!_settings.TryGetValue(name, out var setting))
                    {
                        _unknownSettings[name] = value;
                        continue;
                    }
                    try
                    {
                        if (!setting.SetFromJson(value, skipSave: true))
                            setting.Reset(skipSave: true);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Invalid value for setting {SettingName}; resetting to default.", name);
                        setting.Reset(skipSave: true);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                _loadError = exception;
                logger.LogError(exception, "Failed to load settings file: {Path}", configPath);
            }
            finally
            {
                _loaded = true;
            }
        }
    }
}
