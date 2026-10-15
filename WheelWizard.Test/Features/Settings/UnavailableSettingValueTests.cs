using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.Settings;

/// <summary>
/// A path setting whose folder is not there at load (an unplugged drive) falls back to its default, but the
/// choice must not be forgotten: it still counts as configured, and saving something else must not erase it.
/// </summary>
public class UnavailableSettingValueTests
{
    private const string Config = "/config/config.json";

    private static (WhWzSettingManager Manager, WhWzSetting<string> Path, WhWzSetting<bool> Other, MockFileSystem Fs) Create(
        string json,
        Func<string, bool> pathExists
    )
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        fs.File.WriteAllText(Config, json);
        var manager = new WhWzSettingManager(NullLogger<WhWzSettingManager>.Instance, fs);
        var path = new WhWzSetting<string>("Folder", "", setting => manager.SaveSettings(Config, setting)).SetValidation(pathExists);
        path.SetRetainUnavailable();
        var other = new WhWzSetting<bool>("Other", false, setting => manager.SaveSettings(Config, setting));
        manager.RegisterSetting(path);
        manager.RegisterSetting(other);
        manager.LoadSettings(Config);
        return (manager, path, other, fs);
    }

    [Fact]
    public void AFolderThatIsNotThere_IsNotTheValue_ButItIsRemembered()
    {
        var (_, path, _, _) = Create("{\"Folder\":\"/usb/Dolphin\"}", _ => false);

        Assert.Equal("", path.Get());
        Assert.False(path.IsValid());
        Assert.Equal("/usb/Dolphin", path.UnavailableValue);
        Assert.True(path.HasUnavailableValue);
    }

    [Fact]
    public void SavingAnotherSetting_WhileTheFolderIsAway_WritesTheChoiceBack()
    {
        var (_, _, other, fs) = Create("{\"Folder\":\"/usb/Dolphin\",\"Unknown\":7}", _ => false);

        Assert.True(other.Set(true));

        var saved = fs.File.ReadAllText(Config);
        Assert.Contains("\"Folder\": \"/usb/Dolphin\"", saved);
        Assert.Contains("\"Unknown\": 7", saved);
        Assert.Contains("\"Other\": true", saved);
    }

    [Fact]
    public void PickingANewFolder_ReplacesTheRememberedOne()
    {
        var (_, path, _, fs) = Create("{\"Folder\":\"/usb/Dolphin\"}", value => value == "/home/Dolphin");

        Assert.True(path.Set("/home/Dolphin"));

        Assert.False(path.HasUnavailableValue);
        Assert.Contains("\"Folder\": \"/home/Dolphin\"", fs.File.ReadAllText(Config));
    }

    [Fact]
    public void ResettingTheSetting_ForgetsTheRememberedFolder()
    {
        var (_, path, other, fs) = Create("{\"Folder\":\"/usb/Dolphin\"}", _ => false);

        path.Reset();
        other.Set(true);

        Assert.False(path.HasUnavailableValue);
        Assert.Contains("\"Folder\": \"\"", fs.File.ReadAllText(Config));
    }

    [Fact]
    public void ARejectedFolder_DoesNotEraseTheRememberedOne()
    {
        var (_, path, other, fs) = Create("{\"Folder\":\"/usb/Dolphin\"}", value => value == "/home/Dolphin");

        Assert.False(path.Set("/elsewhere/Dolphin"));
        other.Set(true);

        Assert.True(path.HasUnavailableValue);
        Assert.Contains("\"Folder\": \"/usb/Dolphin\"", fs.File.ReadAllText(Config));
    }

    [Fact]
    public void ValueSettingsWithANonDefaultDefault_AreWrittenAsTheyAre()
    {
        // default(T) is not "nothing remembered": a bool that defaults to true must not be saved as false.
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        fs.File.WriteAllText(Config, "{}");
        var manager = new WhWzSettingManager(NullLogger<WhWzSettingManager>.Instance, fs);
        var animations = new WhWzSetting<bool>("EnableAnimations", true, setting => manager.SaveSettings(Config, setting));
        var scale = new WhWzSetting<double>("WindowScale", 1.0, setting => manager.SaveSettings(Config, setting));
        var other = new WhWzSetting<bool>("Other", false, setting => manager.SaveSettings(Config, setting));
        manager.RegisterSetting(animations);
        manager.RegisterSetting(scale);
        manager.RegisterSetting(other);
        manager.LoadSettings(Config);

        other.Set(true);

        Assert.False(animations.HasUnavailableValue);
        var saved = fs.File.ReadAllText(Config);
        Assert.Contains("\"EnableAnimations\": true", saved);
        Assert.Contains("\"WindowScale\": 1", saved);
    }

    [Fact]
    public void AFolderThatIsThere_IsJustTheValue()
    {
        var (_, path, _, _) = Create("{\"Folder\":\"/home/Dolphin\"}", _ => true);

        Assert.Equal("/home/Dolphin", path.Get());
        Assert.False(path.HasUnavailableValue);
    }

    [Fact]
    public void OnlySettingsThatAskForIt_RememberRejectedValues()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        fs.File.WriteAllText(Config, "{\"Language\":\"xx\"}");
        var manager = new WhWzSettingManager(NullLogger<WhWzSettingManager>.Instance, fs);
        var language = new WhWzSetting<string>("Language", "en", setting => manager.SaveSettings(Config, setting)).SetValidation(value =>
            value == "en"
        );
        var other = new WhWzSetting<bool>("Other", false, setting => manager.SaveSettings(Config, setting));
        manager.RegisterSetting(language);
        manager.RegisterSetting(other);
        manager.LoadSettings(Config);

        Assert.Equal("en", language.Get());
        Assert.False(language.HasUnavailableValue);
        other.Set(true);
        Assert.DoesNotContain("xx", fs.File.ReadAllText(Config));
    }

    [Fact]
    public void ASettingsFileThatCannotBeRead_IsReported_AndLeftAlone()
    {
        var (manager, path, other, fs) = Create("{ broken", _ => true);

        Assert.True(manager.HadLoadError);
        Assert.False(other.Set(true));
        Assert.Equal("{ broken", fs.File.ReadAllText(Config));
        Assert.Equal("", path.Get());
    }

    [Fact]
    public void ADolphinIniLoadPath_ThatIsNotThere_IsRememberedToo()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/dolphin/Config");
        fs.File.WriteAllText("/dolphin/Config/Dolphin.ini", "[General]\nLoadPath = /usb/Load\n");
        var manager = new DolphinSettingManager(fs);
        var loadPath = new DolphinSetting<string>(("Dolphin.ini", "General", "LoadPath"), "").SetValidation(value =>
            fs.Directory.Exists(value)
        );
        loadPath.SetRetainUnavailable();
        manager.RegisterSetting(loadPath);

        manager.LoadSettings("/dolphin/Config");

        Assert.Equal("", loadPath.Get());
        Assert.Equal("/usb/Load", loadPath.UnavailableValue);
        // Dolphin's own file is never rewritten by loading.
        Assert.Equal("[General]\nLoadPath = /usb/Load\n", fs.File.ReadAllText("/dolphin/Config/Dolphin.ini"));
    }
}
