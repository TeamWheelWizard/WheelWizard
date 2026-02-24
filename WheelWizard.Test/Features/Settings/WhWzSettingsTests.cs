using System.Text.Json;
using Microsoft.Extensions.Logging;
using Testably.Abstractions.Testing;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.Settings;

[Collection("SettingsFeature")]
public class WhWzSettingTests
{
    [Fact]
    public void Set_StoresValueAndCallsSaveAction_WhenValueIsValid()
    {
        var saveCalls = 0;
        var setting = new WhWzSetting(typeof(int), "Volume", 10, _ => saveCalls++);

        var result = setting.Set(20);

        Assert.True(result);
        Assert.Equal(20, Assert.IsType<int>(setting.Get()));
        Assert.Equal(1, saveCalls);
    }

    [Fact]
    public void Set_ReturnsFalseAndKeepsOldValue_WhenValidationFails()
    {
        var setting = new WhWzSetting(typeof(int), "Volume", 10).SetValidation(value => (int)value! >= 0);
        setting.Set(5);

        var result = setting.Set(-1);

        Assert.False(result);
        Assert.Equal(5, Assert.IsType<int>(setting.Get()));
    }

    [Fact]
    public void Reset_AppliesDefaultValue_EvenIfDefaultDoesNotPassValidation()
    {
        var saveCalls = 0;
        var setting = new WhWzSetting(typeof(int), "Threshold", 5, _ => saveCalls++).SetValidation(value => (int)value! >= 10);
        setting.Set(12);

        setting.Reset();

        Assert.Equal(5, Assert.IsType<int>(setting.Get()));
        Assert.Equal(2, saveCalls);
    }

    [Fact]
    public void SetFromJson_ParsesEnumAndArrayValues()
    {
        var enumSetting = new WhWzSetting(typeof(DayOfWeek), "Day", DayOfWeek.Monday);
        var arraySetting = new WhWzSetting(typeof(string[]), "Names", Array.Empty<string>());
        using var enumDocument = JsonDocument.Parse("2");
        using var arrayDocument = JsonDocument.Parse("[\"A\", \"B\"]");

        var enumResult = enumSetting.SetFromJson(enumDocument.RootElement, skipSave: true);
        var arrayResult = arraySetting.SetFromJson(arrayDocument.RootElement, skipSave: true);

        Assert.True(enumResult);
        Assert.True(arrayResult);
        Assert.Equal(DayOfWeek.Tuesday, Assert.IsType<DayOfWeek>(enumSetting.Get()));
        Assert.Equal(["A", "B"], Assert.IsType<string[]>(arraySetting.Get()));
    }

    [Fact]
    public void SetFromJson_Throws_WhenTypeIsUnsupported()
    {
        var setting = new WhWzSetting(typeof(decimal), "Price", 1m);
        using var document = JsonDocument.Parse("2");

        Assert.Throws<InvalidOperationException>(() => setting.SetFromJson(document.RootElement, skipSave: true));
    }
}

[Collection("SettingsFeature")]
public class WhWzSettingManagerTests
{
    [Fact]
    public void LoadSettings_AppliesPersistedValues_ToRegisteredSettings()
    {
        var fileSystem = new MockFileSystem();
        var logger = Substitute.For<ILogger<WhWzSettingManager>>();
        var manager = new WhWzSettingManager(logger, fileSystem);
        var volume = new WhWzSetting(typeof(int), "Volume", 5).SetValidation(value => (int)value! >= 0);
        var language = new WhWzSetting(typeof(string), "Language", "en");
        var configPath = PathManager.WheelWizardConfigFilePath;
        var configFolderPath = fileSystem.Path.GetDirectoryName(configPath)!;
        fileSystem.Directory.CreateDirectory(configFolderPath);
        fileSystem.File.WriteAllText(configPath, "{\"Volume\":12,\"Language\":\"de\",\"Unknown\":true}");

        manager.RegisterSetting(volume);
        manager.RegisterSetting(language);
        manager.LoadSettings();

        Assert.Equal(12, Assert.IsType<int>(volume.Get()));
        Assert.Equal("de", Assert.IsType<string>(language.Get()));
    }

    [Fact]
    public void LoadSettings_ResetsInvalidPersistedValues_ToDefaults()
    {
        var fileSystem = new MockFileSystem();
        var logger = Substitute.For<ILogger<WhWzSettingManager>>();
        var manager = new WhWzSettingManager(logger, fileSystem);
        var volume = new WhWzSetting(typeof(int), "Volume", 5).SetValidation(value => (int)value! >= 0);
        var configPath = PathManager.WheelWizardConfigFilePath;
        var configFolderPath = fileSystem.Path.GetDirectoryName(configPath)!;
        fileSystem.Directory.CreateDirectory(configFolderPath);
        fileSystem.File.WriteAllText(configPath, "{\"Volume\":-1}");

        manager.RegisterSetting(volume);
        manager.LoadSettings();

        Assert.Equal(5, Assert.IsType<int>(volume.Get()));
    }

    [Fact]
    public void SaveSettings_PersistsRegisteredValues_AfterLoad()
    {
        var fileSystem = new MockFileSystem();
        var logger = Substitute.For<ILogger<WhWzSettingManager>>();
        var manager = new WhWzSettingManager(logger, fileSystem);
        var volume = new WhWzSetting(typeof(int), "Volume", 5);
        var configPath = PathManager.WheelWizardConfigFilePath;

        manager.RegisterSetting(volume);
        manager.LoadSettings();
        volume.Set(9, skipSave: true);
        manager.SaveSettings(volume);

        var savedJson = fileSystem.File.ReadAllText(configPath);
        Assert.Contains("\"Volume\": 9", savedJson);
    }

    [Fact]
    public void RegisterSetting_IsIgnoredAfterLoad()
    {
        var fileSystem = new MockFileSystem();
        var logger = Substitute.For<ILogger<WhWzSettingManager>>();
        var manager = new WhWzSettingManager(logger, fileSystem);
        var registeredBeforeLoad = new WhWzSetting(typeof(int), "Volume", 1);
        var ignoredAfterLoad = new WhWzSetting(typeof(string), "Future", "initial");
        var configPath = PathManager.WheelWizardConfigFilePath;

        manager.RegisterSetting(registeredBeforeLoad);
        manager.LoadSettings();
        manager.RegisterSetting(ignoredAfterLoad);
        registeredBeforeLoad.Set(2, skipSave: true);
        ignoredAfterLoad.Set("changed", skipSave: true);
        manager.SaveSettings(registeredBeforeLoad);

        var savedJson = fileSystem.File.ReadAllText(configPath);
        Assert.Contains("\"Volume\": 2", savedJson);
        Assert.DoesNotContain("Future", savedJson);
    }
}
