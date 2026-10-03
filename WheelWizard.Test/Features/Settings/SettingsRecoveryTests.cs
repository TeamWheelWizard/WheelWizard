using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.Settings;

public class SettingsRecoveryTests
{
    [Fact]
    public void DolphinEditPreservesExternalValuesAndReloadDoesNotCarryPreviousProfile()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        fs.File.WriteAllText("/config/GFX.ini", "[Settings]\nShowFPS = False\nInternalResolution = 1\n");
        var manager = new DolphinSettingManager(fs);
        var fps = new DolphinSetting<bool>(("GFX.ini", "Settings", "ShowFPS"), false, s => manager.SaveSettings("/config", s));
        var resolution = new DolphinSetting<int>(("GFX.ini", "Settings", "InternalResolution"), 1);
        manager.RegisterSetting(fps);
        manager.RegisterSetting(resolution);
        manager.LoadSettings("/config");
        fs.File.WriteAllText("/config/GFX.ini", "[Settings]\nShowFPS = False\nInternalResolution = 4\n");
        Assert.True(fps.Set(true));
        Assert.Contains("InternalResolution = 4", fs.File.ReadAllText("/config/GFX.ini"));
        Assert.Contains("ShowFPS = False", fs.File.ReadAllText("/config/GFX.ini.bak"));
        manager.ReloadSettings("/config");
        Assert.Equal(4, resolution.Get());
        fs.File.WriteAllText("/config/GFX.ini", "[Settings]\nInternalResolution = invalid\n");
        manager.ReloadSettings("/config");
        Assert.Equal(1, resolution.Get());
        Assert.Equal(false, fps.Get());
        Assert.Contains("invalid", fs.File.ReadAllText("/config/GFX.ini"));
    }

    [Fact]
    public void RecompReloadMissingKeyUsesDefault()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        fs.File.WriteAllText("/config/Config.toml", "[video]\nshow_fps = false\n");
        var manager = new RecompSettingManager(fs);
        var setting = new RecompSetting<bool>(("video", "show_fps"), true, s => manager.SaveSettings("/config/Config.toml", s));
        manager.RegisterSetting(setting);
        manager.LoadSettings("/config/Config.toml");
        fs.File.WriteAllText("/config/Config.toml", "[video]\n");
        manager.ReloadSettings("/config/Config.toml");
        Assert.Equal(true, setting.Get());
        fs.File.Delete("/config/Config.toml");
        Assert.False(setting.Set(false));
        Assert.Equal(true, setting.Get());
    }

    [Fact]
    public void FailedSaveRollsBackAndCanBeRetried()
    {
        var fail = true;
        var setting = new WhWzSetting<bool>(
            "Enabled",
            false,
            _ =>
            {
                if (fail)
                    throw new IOException("disk full");
            }
        );
        var notifications = 0;
        setting.Changed += _ => notifications++;
        Assert.False(setting.Set(true));
        Assert.Equal(false, setting.Get());
        Assert.Equal(0, notifications);
        fail = false;
        Assert.True(setting.Set(true));
        Assert.Null(setting.SaveError);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void JsonRetainsExistingKeysAndUnknownValuesAndPreservesCorruptFile()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        const string path = "/config/config.json";
        const string original = "{\"EnableAnimations\":false,\"FutureSetting\":{\"value\":2}}";
        fs.File.WriteAllText(path, original);
        var manager = new WhWzSettingManager(NullLogger<WhWzSettingManager>.Instance, fs);
        var setting = new WhWzSetting<bool>("EnableAnimations", true, s => manager.SaveSettings(path, s));
        manager.RegisterSetting(setting);
        manager.LoadSettings(path);
        Assert.Equal(false, setting.Get());
        Assert.True(setting.Set(true));
        Assert.Contains("FutureSetting", fs.File.ReadAllText(path));
        Assert.Equal(original, fs.File.ReadAllText(path + ".bak"));
        fs.File.WriteAllText(path, "broken json");
        var corrupt = new WhWzSettingManager(NullLogger<WhWzSettingManager>.Instance, fs);
        corrupt.RegisterSetting(setting);
        corrupt.LoadSettings(path);
        Assert.Throws<IOException>(() => corrupt.SaveSettings(path, setting));
        Assert.Equal("broken json", fs.File.ReadAllText(path));
    }

    [Fact]
    public void VirtualSettingTracksDependenciesAfterSetterFailure()
    {
        var source = new WhWzSetting<int>("source", 1);
        using var derived = new VirtualSetting<int>(_ => throw new IOException("save failed"), source.Get).SetDependencies(source);
        Assert.False(derived.Set(2));
        source.Set(3);
        Assert.Equal(3, derived.Get());
    }
}
