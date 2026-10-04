using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.Settings;

public class SettingsPersistencePathTests
{
    [Fact]
    public void ApplicationSettings_SaveToRequestedDestination_WithoutChangingOriginal()
    {
        var fs = new MockFileSystem();
        var original = fs.Path.GetFullPath("/original/config.json");
        var relocated = fs.Path.GetFullPath("/relocated/config.json");
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(original)!);
        const string originalJson = "{\"Volume\":12}";
        fs.File.WriteAllText(original, originalJson);
        var manager = new WhWzSettingManager(NullLogger<WhWzSettingManager>.Instance, fs);
        var setting = new WhWzSetting(typeof(int), "Volume", 5);
        manager.RegisterSetting(setting);

        manager.LoadSettings(original);
        Assert.Equal(12, setting.Get());
        setting.Set(20, skipSave: true);
        manager.SaveSettings(relocated, setting);

        Assert.Equal(originalJson, fs.File.ReadAllText(original));
        Assert.Contains("\"Volume\": 20", fs.File.ReadAllText(relocated));
    }

    [Fact]
    public void DolphinSettings_ReloadAndSave_UseNewUserDirectory()
    {
        var fs = new MockFileSystem();
        var first = fs.Path.GetFullPath("/first/Config");
        var second = fs.Path.GetFullPath("/second/Config");
        fs.Directory.CreateDirectory(first);
        fs.Directory.CreateDirectory(second);
        var firstFile = fs.Path.Combine(first, "Dolphin.ini");
        var secondFile = fs.Path.Combine(second, "Dolphin.ini");
        const string originalIni = "[General]\nNANDRootPath = /first-nand\n";
        fs.File.WriteAllText(firstFile, originalIni);
        fs.File.WriteAllText(secondFile, "[General]\nNANDRootPath = /second-nand\nOther = keep\n");
        var manager = new DolphinSettingManager(fs);
        var setting = new DolphinSetting(typeof(string), ("Dolphin.ini", "General", "NANDRootPath"), "");
        manager.RegisterSetting(setting);

        manager.LoadSettings(first);
        manager.ReloadSettings(second);
        Assert.Equal("/second-nand", setting.Get());
        setting.Set("/updated", skipSave: true);
        manager.SaveSettings(second, setting);

        Assert.Equal(originalIni, fs.File.ReadAllText(firstFile));
        Assert.Contains("NANDRootPath = /updated", fs.File.ReadAllText(secondFile));
        Assert.Contains("Other = keep", fs.File.ReadAllText(secondFile));
    }

    [Fact]
    public void RecompSettings_ReloadSaveAndRemove_PreserveOtherFileAndUnrelatedContent()
    {
        var fs = new MockFileSystem();
        var first = fs.Path.GetFullPath("/first/Config.toml");
        var second = fs.Path.GetFullPath("/second/Config.toml");
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(first)!);
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(second)!);
        const string originalToml = "[paths]\nnand_root = \"first\"\n";
        fs.File.WriteAllText(first, originalToml);
        fs.File.WriteAllLines(
            second,
            ["# keep comment", "[paths]", "nand_root = \"second\"", "other = true", "[video]", "show_fps = false"]
        );
        var manager = new RecompSettingManager(fs);
        var setting = new RecompSetting(typeof(string), ("paths", "nand_root"), "", _ => { });
        manager.RegisterSetting(setting);

        manager.LoadSettings(first);
        manager.ReloadSettings(second);
        Assert.Equal("second", setting.Get());
        setting.Set("updated", skipSave: true);
        manager.SaveSettings(second, setting);
        Assert.Contains("nand_root = \"updated\"", fs.File.ReadAllLines(second));
        manager.RemoveTomlSetting(second, "paths", "nand_root");

        Assert.Equal(originalToml, fs.File.ReadAllText(first));
        Assert.Equal(["# keep comment", "[paths]", "other = true", "[video]", "show_fps = false"], fs.File.ReadAllLines(second));
    }

    [Fact]
    public void RecompSettings_DoNotCreateBackendOwnedFile_WhenMissing()
    {
        var fs = new MockFileSystem();
        var configPath = fs.Path.GetFullPath("/missing/Config.toml");
        var manager = new RecompSettingManager(fs);
        var setting = new RecompSetting(typeof(bool), ("video", "show_fps"), false, _ => { });
        manager.RegisterSetting(setting);

        manager.LoadSettings(configPath);
        manager.SaveSettings(configPath, setting);
        manager.RemoveTomlSetting(configPath, "video", "show_fps");

        Assert.False(fs.File.Exists(configPath));
    }
}
