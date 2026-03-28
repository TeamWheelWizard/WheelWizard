using System.Globalization;
using WheelWizard.Services.Input;
using WheelWizard.Services.Settings;

namespace WheelWizard.Test.Services;

[Collection("SettingsSerial")]
public sealed class MarioKartInputConfigServiceTests
{
    [Fact]
    public void LoadProfile_WhenPadTwoIsActive_ShouldRepresentExistingMappings()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        var originalUserFolder = (string)SettingsManager.USER_FOLDER_PATH.Get();
        try
        {
            File.WriteAllText(
                Path.Combine(configFolder, "GCPadNew.ini"),
                """
                [GCPad1]
                Device = Disabled//
                [GCPad2]
                Device = SDL/1/Test Controller
                Buttons/A = `Button A`
                Buttons/B = `Button B`
                Buttons/X = `Button X`
                Buttons/Z = `Shoulder R`
                Buttons/Start = Start
                Main Stick/Up = `Left Y+`
                Main Stick/Down = `Left Y-`
                Main Stick/Left = `Left X-`
                Main Stick/Right = `Left X+`
                Triggers/L = `Trigger L`
                Triggers/L-Analog = `Trigger L`
                Triggers/R = `Trigger R`
                Triggers/R-Analog = `Trigger R`
                D-Pad/Up = `Pad N`
                D-Pad/Down = `Pad S`
                D-Pad/Left = `Pad W`
                D-Pad/Right = `Pad E`
                Rumble/Motor = `Motor L`
                """
            );

            SettingsManager.USER_FOLDER_PATH.Set(userFolder, skipSave: true);

            var profile = MarioKartInputConfigService.LoadProfile();

            Assert.Equal("GCPad2", profile.SourceSection);
            Assert.Equal("SDL/1/Test Controller", profile.DeviceExpression);
            Assert.Equal("left-stick", profile.Bindings[MarioKartInputAction.Steering]);
            Assert.Equal("`Button A`", profile.Bindings[MarioKartInputAction.Accelerate]);
            Assert.Equal("`Trigger L`", profile.Bindings[MarioKartInputAction.UseItem]);
            Assert.Equal("`Button X` | `Shoulder R`", profile.Bindings[MarioKartInputAction.LookBehind]);
            Assert.Equal("full-dpad", profile.Bindings[MarioKartInputAction.TrickWheelie]);
        }
        finally
        {
            SettingsManager.USER_FOLDER_PATH.Set(originalUserFolder, skipSave: true);
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void SaveProfile_ShouldWriteMarioKartBindingsAndForceSingleControllerSetup()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        var originalUserFolder = (string)SettingsManager.USER_FOLDER_PATH.Get();
        try
        {
            SettingsManager.USER_FOLDER_PATH.Set(userFolder, skipSave: true);

            var profile = new MarioKartInputProfile { DeviceExpression = "SDL/0/Arcade Pad", RumbleBinding = "`Motor L`" };

            profile.Bindings[MarioKartInputAction.Steering] = "dpad";
            profile.Bindings[MarioKartInputAction.Accelerate] = "`Button A`";
            profile.Bindings[MarioKartInputAction.BrakeReverse] = "`Button B`";
            profile.Bindings[MarioKartInputAction.UseItem] = "`Trigger L`";
            profile.Bindings[MarioKartInputAction.Drift] = "`Trigger R`";
            profile.Bindings[MarioKartInputAction.LookBehind] = "`Button X` | `Shoulder R`";
            profile.Bindings[MarioKartInputAction.TrickWheelie] = "`Pad N`";
            profile.Bindings[MarioKartInputAction.Pause] = "Start";

            MarioKartInputConfigService.SaveProfile(profile);

            var gcpadContents = File.ReadAllText(Path.Combine(configFolder, "GCPadNew.ini"));
            var dolphinContents = File.ReadAllText(Path.Combine(configFolder, "Dolphin.ini"));
            var wiimoteContents = File.ReadAllText(Path.Combine(configFolder, "WiimoteNew.ini"));
            var savedProfileContents = File.ReadAllText(Path.Combine(configFolder, "Profiles", "GCPad", "WheelWizard Active.ini"));

            Assert.Contains("Device = SDL/0/Arcade Pad", gcpadContents);
            Assert.Contains("Buttons/A = `Button A`", gcpadContents);
            Assert.Contains("Buttons/B = `Button B`", gcpadContents);
            Assert.Contains("Main Stick/Up = `Pad N`", gcpadContents);
            Assert.Contains("Main Stick/Left = `Pad W`", gcpadContents);
            Assert.Contains("Triggers/L = `Trigger L`", gcpadContents);
            Assert.Contains("Triggers/L-Analog = `Trigger L`", gcpadContents);
            Assert.Contains("Buttons/X = `Button X` | `Shoulder R`", gcpadContents);
            Assert.Contains("Buttons/Z = `Button X` | `Shoulder R`", gcpadContents);
            Assert.Contains("D-Pad/Up = `Pad N`", gcpadContents);
            Assert.Contains("D-Pad/Down = `Pad S`", gcpadContents);
            Assert.Contains("D-Pad/Left = `Pad W`", gcpadContents);
            Assert.Contains("D-Pad/Right = `Pad E`", gcpadContents);
            Assert.Contains("Rumble/Motor = `Motor L`", gcpadContents);

            Assert.Contains("SIDevice0 = 6", dolphinContents);
            Assert.Contains("SIDevice1 = 0", dolphinContents);
            Assert.Contains("SIDevice2 = 0", dolphinContents);
            Assert.Contains("SIDevice3 = 0", dolphinContents);

            Assert.Contains("[Wiimote1]", wiimoteContents);
            Assert.Contains("Device = Disabled//", wiimoteContents);
            Assert.Contains("Source = 0", wiimoteContents);
            Assert.Contains("[Wiimote4]", wiimoteContents);

            Assert.Contains("[Profile]", savedProfileContents);
            Assert.Contains("Device = SDL/0/Arcade Pad", savedProfileContents);
            Assert.Contains("D-Pad/Down = `Pad S`", savedProfileContents);
        }
        finally
        {
            SettingsManager.USER_FOLDER_PATH.Set(originalUserFolder, skipSave: true);
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void LoadProfile_WhenDeadZoneUsesInvariantDecimal_ShouldRoundCorrectly()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        var originalUserFolder = (string)SettingsManager.USER_FOLDER_PATH.Get();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            File.WriteAllText(
                Path.Combine(configFolder, "GCPadNew.ini"),
                """
                [GCPad1]
                Device = SDL/0/Test Controller
                Main Stick/Dead Zone = 24.6
                Main Stick/Up = `Left Y+`
                Main Stick/Down = `Left Y-`
                Main Stick/Left = `Left X-`
                Main Stick/Right = `Left X+`
                """
            );

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            SettingsManager.USER_FOLDER_PATH.Set(userFolder, skipSave: true);

            var profile = MarioKartInputConfigService.LoadProfile();

            Assert.Equal(25, profile.MainStickDeadZonePercent);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            SettingsManager.USER_FOLDER_PATH.Set(originalUserFolder, skipSave: true);
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void SaveProfile_WhenTrickWheelieUsesCustomDirections_ShouldPersistAllDirections()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        var originalUserFolder = (string)SettingsManager.USER_FOLDER_PATH.Get();
        try
        {
            SettingsManager.USER_FOLDER_PATH.Set(userFolder, skipSave: true);

            var profile = new MarioKartInputProfile { DeviceExpression = "SDL/0/Test Controller" };

            profile.Bindings[MarioKartInputAction.Steering] = "left-stick";
            profile.Bindings[MarioKartInputAction.TrickWheelie] = MarioKartInputConfigService.CreateDirectionalBinding(
                MarioKartInputAction.TrickWheelie,
                new DirectionalBindingSet
                {
                    Up = "`Button Y`",
                    Down = "`Button A`",
                    Left = "`Shoulder L`",
                    Right = "`Shoulder R`",
                }
            );

            Assert.Equal(
                "Custom",
                MarioKartInputConfigService.DescribeBinding(
                    MarioKartInputAction.TrickWheelie,
                    profile.Bindings[MarioKartInputAction.TrickWheelie]
                )
            );

            MarioKartInputConfigService.SaveProfile(profile);

            var gcpadContents = File.ReadAllText(Path.Combine(configFolder, "GCPadNew.ini"));

            Assert.Contains("D-Pad/Up = `Button Y`", gcpadContents);
            Assert.Contains("D-Pad/Down = `Button A`", gcpadContents);
            Assert.Contains("D-Pad/Left = `Shoulder L`", gcpadContents);
            Assert.Contains("D-Pad/Right = `Shoulder R`", gcpadContents);
        }
        finally
        {
            SettingsManager.USER_FOLDER_PATH.Set(originalUserFolder, skipSave: true);
            Directory.Delete(userFolder, recursive: true);
        }
    }

    private static string CreateTempUserFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wheelwizard-input-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
