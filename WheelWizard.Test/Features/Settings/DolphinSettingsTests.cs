using Testably.Abstractions.Testing;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Domain;

namespace WheelWizard.Test.Features.Settings;

[Collection("SettingsFeature")]
public class DolphinSettingTests
{
    [Fact]
    public void Constructor_Throws_WhenFileNameIsNotIni()
    {
        var action = () => new DolphinSetting(typeof(string), ("Dolphin.cfg", "General", "NANDRootPath"), "value");

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void SetFromString_ParsesEnumAndFormatsAsIntegerString()
    {
        var setting = new DolphinSetting(
            typeof(DolphinShaderCompilationMode),
            ("GFX.ini", "Settings", "ShaderCompilationMode"),
            DolphinShaderCompilationMode.Default
        );

        var result = setting.SetFromString("2", skipSave: true);

        Assert.True(result);
        Assert.Equal(DolphinShaderCompilationMode.HybridUberShaders, Assert.IsType<DolphinShaderCompilationMode>(setting.Get()));
        Assert.Equal("2", setting.GetStringValue());
    }

    [Fact]
    public void Set_ReturnsFalseAndKeepsOldValue_WhenValidationFails()
    {
        var setting = new DolphinSetting(typeof(int), ("GFX.ini", "Settings", "InternalResolution"), 1).SetValidation(value =>
            (int)value! >= 0
        );
        setting.Set(2);

        var result = setting.Set(-1);

        Assert.False(result);
        Assert.Equal(2, Assert.IsType<int>(setting.Get()));
    }

    [Fact]
    public void SetFromString_Throws_WhenTypeIsUnsupported()
    {
        var setting = new DolphinSetting(typeof(decimal), ("GFX.ini", "Settings", "Price"), 1m);

        Assert.Throws<InvalidOperationException>(() => setting.SetFromString("3.14"));
    }
}

[Collection("SettingsFeature")]
public class DolphinSettingManagerTests
{
    [Fact]
    public void LoadSettings_ReadsExistingValue_FromIniFile()
    {
        var fileSystem = new MockFileSystem();
        var userFolderPath = $"/wheelwizard-user-{Guid.NewGuid():N}";
        SettingsTestUtils.InitializeSettingsRuntime(userFolderPath);
        var configFolderPath = PathManager.ConfigFolderPath;
        var iniPath = Path.Combine(configFolderPath, "Dolphin.ini");
        fileSystem.Directory.CreateDirectory(configFolderPath);
        fileSystem.File.WriteAllLines(iniPath, ["[General]", "NANDRootPath = /persisted"]);
        var manager = new DolphinSettingManager(fileSystem);
        var setting = new DolphinSetting(typeof(string), ("Dolphin.ini", "General", "NANDRootPath"), "/default");

        manager.RegisterSetting(setting);
        manager.LoadSettings();

        Assert.Equal("/persisted", Assert.IsType<string>(setting.Get()));
    }

    [Fact]
    public void LoadSettings_WritesDefaultValue_WhenIniEntryIsMissing()
    {
        var fileSystem = new MockFileSystem();
        var userFolderPath = $"/wheelwizard-user-{Guid.NewGuid():N}";
        SettingsTestUtils.InitializeSettingsRuntime(userFolderPath);
        var configFolderPath = PathManager.ConfigFolderPath;
        var iniPath = Path.Combine(configFolderPath, "Dolphin.ini");
        fileSystem.Directory.CreateDirectory(configFolderPath);
        fileSystem.File.WriteAllLines(iniPath, ["[General]", "OtherSetting = 1"]);
        var manager = new DolphinSettingManager(fileSystem);
        var setting = new DolphinSetting(typeof(string), ("Dolphin.ini", "General", "NANDRootPath"), "/default");

        manager.RegisterSetting(setting);
        manager.LoadSettings();

        var updatedFile = fileSystem.File.ReadAllText(iniPath);
        Assert.Contains("NANDRootPath = /default", updatedFile);
    }

    [Fact]
    public void SaveSettings_UpdatesExistingSettingLine_InIniFile()
    {
        var fileSystem = new MockFileSystem();
        var userFolderPath = $"/wheelwizard-user-{Guid.NewGuid():N}";
        SettingsTestUtils.InitializeSettingsRuntime(userFolderPath);
        var configFolderPath = PathManager.ConfigFolderPath;
        var iniPath = Path.Combine(configFolderPath, "Dolphin.ini");
        fileSystem.Directory.CreateDirectory(configFolderPath);
        fileSystem.File.WriteAllLines(iniPath, ["[General]", "NANDRootPath = /old"]);
        var manager = new DolphinSettingManager(fileSystem);
        var setting = new DolphinSetting(typeof(string), ("Dolphin.ini", "General", "NANDRootPath"), "/default");

        manager.RegisterSetting(setting);
        manager.LoadSettings();
        setting.Set("/new", skipSave: true);
        manager.SaveSettings(setting);

        var updatedFile = fileSystem.File.ReadAllText(iniPath);
        Assert.Contains("NANDRootPath = /new", updatedFile);
        Assert.DoesNotContain("NANDRootPath = /old", updatedFile);
    }

    [Fact]
    public void ReloadSettings_ReReadsFile_AfterItChangesOnDisk()
    {
        var fileSystem = new MockFileSystem();
        var userFolderPath = $"/wheelwizard-user-{Guid.NewGuid():N}";
        SettingsTestUtils.InitializeSettingsRuntime(userFolderPath);
        var configFolderPath = PathManager.ConfigFolderPath;
        var iniPath = Path.Combine(configFolderPath, "Dolphin.ini");
        fileSystem.Directory.CreateDirectory(configFolderPath);
        fileSystem.File.WriteAllLines(iniPath, ["[General]", "NANDRootPath = /first"]);
        var manager = new DolphinSettingManager(fileSystem);
        var setting = new DolphinSetting(typeof(string), ("Dolphin.ini", "General", "NANDRootPath"), "/default");

        manager.RegisterSetting(setting);
        manager.LoadSettings();
        fileSystem.File.WriteAllLines(iniPath, ["[General]", "NANDRootPath = /second"]);
        manager.ReloadSettings();

        Assert.Equal("/second", Assert.IsType<string>(setting.Get()));
    }
}
