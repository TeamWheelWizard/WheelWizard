using System.Globalization;
using WheelWizard.Services.Input;
using WheelWizard.Test.Features.Settings;

namespace WheelWizard.Test.Services;

[Collection("SettingsFeature")]
public sealed class MarioKartInputConfigServiceTests
{
    [Fact]
    public void LoadProfile_WhenPadTwoIsActive_ShouldRepresentExistingMappings()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

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

            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

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
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void SaveProfile_ShouldWriteMarioKartBindingsAndForceSingleControllerSetup()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        try
        {
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

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
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void LoadProfile_WhenDeadZoneUsesInvariantDecimal_ShouldRoundCorrectly()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

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
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var profile = MarioKartInputConfigService.LoadProfile();

            Assert.Equal(25, profile.MainStickDeadZonePercent);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void SaveProfile_WhenTrickWheelieUsesCustomDirections_ShouldPersistAllDirections()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        try
        {
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

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
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void SaveProfile_WhenRumbleIsDisabled_ShouldPersistRumbleAsDisabled()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        try
        {
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var profile = new MarioKartInputProfile { DeviceExpression = "SDL/0/Test Controller", RumbleBinding = string.Empty };

            MarioKartInputConfigService.SaveProfile(profile);

            var gcpadContents = File.ReadAllText(Path.Combine(configFolder, "GCPadNew.ini"));
            var savedProfileContents = File.ReadAllText(Path.Combine(configFolder, "Profiles", "GCPad", "WheelWizard Active.ini"));

            Assert.Contains("Rumble/Motor =", gcpadContents);
            Assert.DoesNotContain("Rumble/Motor = `Motor` | `Motor L` | `Motor R` | `Strong` | `Weak`", gcpadContents);
            Assert.Contains("Rumble/Motor =", savedProfileContents);
            Assert.DoesNotContain("Rumble/Motor = `Motor` | `Motor L` | `Motor R` | `Strong` | `Weak`", savedProfileContents);
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void CreateAutoMappedProfile_ShouldPreserveExistingRumblePreference()
    {
        var controller = new ControllerDeviceOption(
            1,
            0,
            "Test Controller",
            "Subtitle",
            "SDL/0/Test Controller",
            SDL3.SDL.GamepadType.XboxOne,
            IsConnected: true
        );

        var disabledRumbleProfile = new MarioKartInputProfile { RumbleBinding = string.Empty };
        var enabledRumbleProfile = new MarioKartInputProfile { RumbleBinding = "`Motor L`" };

        var autoMappedWithDisabledRumble = MarioKartInputConfigService.CreateAutoMappedProfile(controller, disabledRumbleProfile);
        var autoMappedWithEnabledRumble = MarioKartInputConfigService.CreateAutoMappedProfile(controller, enabledRumbleProfile);

        Assert.False(MarioKartInputConfigService.IsRumbleEnabled(autoMappedWithDisabledRumble));
        Assert.True(MarioKartInputConfigService.IsRumbleEnabled(autoMappedWithEnabledRumble));
        Assert.Equal("`Motor L`", autoMappedWithEnabledRumble.RumbleBinding);
    }

    [Fact]
    public void CreateAutoMappedProfile_WhenKeyboardIsSelected_ShouldApplyKeyboardLayout()
    {
        var currentProfile = new MarioKartInputProfile { RumbleBinding = "`Motor L`" };

        var autoMappedProfile = MarioKartInputConfigService.CreateAutoMappedProfile(
            KeyboardInputService.CreateKeyboardOption(),
            currentProfile
        );

        var steeringBindings = MarioKartInputConfigService.GetDirectionalBindingSet(
            MarioKartInputAction.Steering,
            autoMappedProfile.Bindings[MarioKartInputAction.Steering]
        );
        var trickBindings = MarioKartInputConfigService.GetDirectionalBindingSet(
            MarioKartInputAction.TrickWheelie,
            autoMappedProfile.Bindings[MarioKartInputAction.TrickWheelie]
        );

        Assert.Equal(KeyboardInputService.GetKeyboardDeviceExpression(), autoMappedProfile.DeviceExpression);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("UP"), steeringBindings.Up);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("DOWN"), steeringBindings.Down);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("LEFT"), steeringBindings.Left);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("RIGHT"), steeringBindings.Right);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("X"), autoMappedProfile.Bindings[MarioKartInputAction.Accelerate]);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("Z"), autoMappedProfile.Bindings[MarioKartInputAction.BrakeReverse]);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("C"), autoMappedProfile.Bindings[MarioKartInputAction.UseItem]);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("LSHIFT"), autoMappedProfile.Bindings[MarioKartInputAction.Drift]);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("Q"), autoMappedProfile.Bindings[MarioKartInputAction.LookBehind]);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("W"), trickBindings.Up);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("S"), trickBindings.Down);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("A"), trickBindings.Left);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("D"), trickBindings.Right);
        Assert.Equal(KeyboardInputService.CreateAbsoluteBinding("RETURN"), autoMappedProfile.Bindings[MarioKartInputAction.Pause]);
        Assert.Equal("`Motor L`", autoMappedProfile.RumbleBinding);
    }

    [Fact]
    public void DescribeBinding_WhenLookBehindHasMultipleBindings_ShouldReturnCustom()
    {
        var description = MarioKartInputConfigService.DescribeBinding(MarioKartInputAction.LookBehind, "`Button X` | `Shoulder R`");

        Assert.Equal("Custom", description);
    }

    [Fact]
    public void DescribeBinding_WhenBindingUsesKeyboardToken_ShouldHumanizeTheKey()
    {
        var description = MarioKartInputConfigService.DescribeBinding(
            MarioKartInputAction.Pause,
            KeyboardInputService.CreateAbsoluteBinding("RETURN")
        );

        Assert.Equal("Enter", description);
    }

    [Fact]
    public void CreateCombinedBinding_ShouldRemoveDuplicatesAndPreserveOrder()
    {
        var binding = MarioKartInputConfigService.CreateCombinedBinding("`Button X`", "`Shoulder R`", "`Button X`");

        Assert.Equal("`Button X` | `Shoulder R`", binding);
    }

    [Fact]
    public void SaveProfile_WhenActionUsesMultipleBindings_ShouldWriteCombinedExpression()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        try
        {
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var profile = new MarioKartInputProfile { DeviceExpression = "SDL/0/Test Controller" };
            profile.Bindings[MarioKartInputAction.Accelerate] = "`Button A` | `Trigger R`";
            profile.Bindings[MarioKartInputAction.Pause] = "Start | Back";

            MarioKartInputConfigService.SaveProfile(profile);

            var gcpadContents = File.ReadAllText(Path.Combine(configFolder, "GCPadNew.ini"));

            Assert.Contains("Buttons/A = `Button A` | `Trigger R`", gcpadContents);
            Assert.Contains("Buttons/Start = Start | Back", gcpadContents);
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void SaveProfile_WhenDirectionalBindingsUseMultipleInputs_ShouldPersistEachDirection()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        try
        {
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var profile = new MarioKartInputProfile { DeviceExpression = "SDL/0/Test Controller" };
            profile.Bindings[MarioKartInputAction.TrickWheelie] = MarioKartInputConfigService.CreateDirectionalBinding(
                MarioKartInputAction.TrickWheelie,
                new DirectionalBindingSet
                {
                    Up = "`Pad N` | `Button Y`",
                    Down = "`Pad S` | `Button A`",
                    Left = "`Pad W` | `Shoulder L`",
                    Right = "`Pad E` | `Shoulder R`",
                }
            );

            MarioKartInputConfigService.SaveProfile(profile);

            var gcpadContents = File.ReadAllText(Path.Combine(configFolder, "GCPadNew.ini"));

            Assert.Contains("D-Pad/Up = `Pad N` | `Button Y`", gcpadContents);
            Assert.Contains("D-Pad/Down = `Pad S` | `Button A`", gcpadContents);
            Assert.Contains("D-Pad/Left = `Pad W` | `Shoulder L`", gcpadContents);
            Assert.Contains("D-Pad/Right = `Pad E` | `Shoulder R`", gcpadContents);
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void LoadProfile_WhenDirectionalBindingsUseMultipleInputs_ShouldKeepCombinedDirections()
    {
        var userFolder = CreateTempUserFolder();
        var configFolder = Path.Combine(userFolder, "Config");
        Directory.CreateDirectory(configFolder);

        try
        {
            File.WriteAllText(
                Path.Combine(configFolder, "GCPadNew.ini"),
                """
                [GCPad1]
                Device = SDL/0/Test Controller
                Main Stick/Up = `Left Y+`
                Main Stick/Down = `Left Y-`
                Main Stick/Left = `Left X-`
                Main Stick/Right = `Left X+`
                D-Pad/Up = `Pad N` | `Button Y`
                D-Pad/Down = `Pad S` | `Button A`
                D-Pad/Left = `Pad W` | `Shoulder L`
                D-Pad/Right = `Pad E` | `Shoulder R`
                """
            );

            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var profile = MarioKartInputConfigService.LoadProfile();
            var bindings = MarioKartInputConfigService.GetDirectionalBindingSet(
                MarioKartInputAction.TrickWheelie,
                profile.Bindings[MarioKartInputAction.TrickWheelie]
            );

            Assert.Equal("`Pad N` | `Button Y`", bindings.Up);
            Assert.Equal("`Pad S` | `Button A`", bindings.Down);
            Assert.Equal("`Pad W` | `Shoulder L`", bindings.Left);
            Assert.Equal("`Pad E` | `Shoulder R`", bindings.Right);
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void GetPresetOptions_WhenDolphinProfilesExist_ShouldExposeCurrentAndSavedProfiles()
    {
        var userFolder = CreateTempUserFolder();
        var profilesFolder = Path.Combine(userFolder, "Config", "Profiles", "GCPad");
        Directory.CreateDirectory(profilesFolder);

        try
        {
            File.WriteAllText(Path.Combine(profilesFolder, "WheelWizard Active.ini"), "[Profile]\nDevice = SDL/0/Test Controller\n");
            File.WriteAllText(Path.Combine(profilesFolder, "Arcade Pad.ini"), "[Profile]\nDevice = SDL/1/Arcade Pad\n");

            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var options = MarioKartInputConfigService.GetPresetOptions();

            Assert.Equal(MarioKartInputPresetKind.CurrentDolphinSettings, options[0].Kind);
            Assert.Contains(options, option => option.DisplayName == "Wheel Wizard Active");
            Assert.Contains(options, option => option.DisplayName == "Arcade Pad");
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void LoadProfile_WhenPresetPointsToSavedDolphinProfile_ShouldLoadProfileSection()
    {
        var userFolder = CreateTempUserFolder();
        var profilesFolder = Path.Combine(userFolder, "Config", "Profiles", "GCPad");
        Directory.CreateDirectory(profilesFolder);

        try
        {
            File.WriteAllText(
                Path.Combine(profilesFolder, "Arcade Pad.ini"),
                """
                [Profile]
                Device = SDL/1/Arcade Pad
                Buttons/A = `Button A`
                Buttons/B = `Button B`
                Main Stick/Up = `Left Y+`
                Main Stick/Down = `Left Y-`
                Main Stick/Left = `Left X-`
                Main Stick/Right = `Left X+`
                """
            );

            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var preset = MarioKartInputConfigService.GetPresetOptions().Single(option => option.DisplayName == "Arcade Pad");
            var profile = MarioKartInputConfigService.LoadProfile(preset);

            Assert.Equal("Profile", profile.SourceSection);
            Assert.Equal("SDL/1/Arcade Pad", profile.DeviceExpression);
            Assert.Equal("left-stick", profile.Bindings[MarioKartInputAction.Steering]);
            Assert.Equal("`Button A`", profile.Bindings[MarioKartInputAction.Accelerate]);
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void SaveNamedPreset_ShouldPersistCurrentProfileAsNamedProfile()
    {
        var userFolder = CreateTempUserFolder();
        var profilesFolder = Path.Combine(userFolder, "Config", "Profiles", "GCPad");

        try
        {
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var profile = new MarioKartInputProfile { DeviceExpression = "SDL/0/Test Controller" };
            profile.Bindings[MarioKartInputAction.Steering] = "left-stick";
            profile.Bindings[MarioKartInputAction.Accelerate] = "`Button A`";

            var result = MarioKartInputConfigService.SaveNamedPreset(profile, "Arcade Pad");

            Assert.True(result.IsSuccess);
            Assert.Equal(Path.Combine(profilesFolder, "Arcade Pad.ini"), result.Value);
            Assert.Contains("Buttons/A = `Button A`", File.ReadAllText(result.Value));
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void DeletePreset_ShouldRemoveSavedPresetFile()
    {
        var userFolder = CreateTempUserFolder();
        var profilesFolder = Path.Combine(userFolder, "Config", "Profiles", "GCPad");
        Directory.CreateDirectory(profilesFolder);
        var presetPath = Path.Combine(profilesFolder, "Arcade Pad.ini");

        try
        {
            File.WriteAllText(presetPath, "[Profile]\nDevice = SDL/1/Arcade Pad\n");
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            var preset = MarioKartInputConfigService.GetPresetOptions().Single(option => option.DisplayName == "Arcade Pad");
            var result = MarioKartInputConfigService.DeletePreset(preset);

            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(presetPath));
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void DoesBindingMatchActiveInputs_WhenMappedButtonIsPressed_ShouldReturnTrue()
    {
        var isActive = MarioKartInputConfigService.DoesBindingMatchActiveInputs(
            MarioKartInputAction.Accelerate,
            "`Button A`",
            new HashSet<string>(StringComparer.Ordinal) { "`Button A`" }
        );

        Assert.True(isActive);
    }

    [Fact]
    public void EnsureLaunchProfileIsApplied_WhenNoConfiguredProfileExists_ShouldNotCreateControllerFiles()
    {
        var userFolder = CreateTempUserFolder();

        try
        {
            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            MarioKartInputConfigService.EnsureLaunchProfileIsApplied();

            Assert.False(File.Exists(Path.Combine(userFolder, "Config", "GCPadNew.ini")));
            Assert.False(File.Exists(Path.Combine(userFolder, "Config", "Dolphin.ini")));
            Assert.False(File.Exists(Path.Combine(userFolder, "Config", "WiimoteNew.ini")));
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void EnsureLaunchProfileIsApplied_WhenActiveProfileExists_ShouldApplySavedProfile()
    {
        var userFolder = CreateTempUserFolder();
        var profilesFolder = Path.Combine(userFolder, "Config", "Profiles", "GCPad");
        Directory.CreateDirectory(profilesFolder);

        try
        {
            File.WriteAllText(
                Path.Combine(profilesFolder, "WheelWizard Active.ini"),
                """
                [Profile]
                Device = SDL/0/Saved Pad
                Buttons/A = `Button A`
                Main Stick/Up = `Left Y+`
                Main Stick/Down = `Left Y-`
                Main Stick/Left = `Left X-`
                Main Stick/Right = `Left X+`
                """
            );

            SettingsTestUtils.InitializeSettingsRuntime(userFolder);

            MarioKartInputConfigService.EnsureLaunchProfileIsApplied();

            var gcpadContents = File.ReadAllText(Path.Combine(userFolder, "Config", "GCPadNew.ini"));
            var dolphinContents = File.ReadAllText(Path.Combine(userFolder, "Config", "Dolphin.ini"));

            Assert.Contains("Device = SDL/0/Saved Pad", gcpadContents);
            Assert.Contains("Buttons/A = `Button A`", gcpadContents);
            Assert.Contains("SIDevice0 = 6", dolphinContents);
        }
        finally
        {
            SettingsTestUtils.ResetSettingsRuntime();
            SettingsTestUtils.ResetSignalRuntime();
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void CreateCombinedBinding_WhenExpressionContainsNestedOr_ShouldPreserveNestedExpression()
    {
        var binding = MarioKartInputConfigService.CreateCombinedBinding("(`Button A` | `Button B`) & `Trigger R`", "`Button X`");

        Assert.Equal("(`Button A` | `Button B`) & `Trigger R` | `Button X`", binding);
    }

    private static string CreateTempUserFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wheelwizard-input-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
