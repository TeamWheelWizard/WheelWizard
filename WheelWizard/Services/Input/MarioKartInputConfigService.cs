using System.Globalization;
using IniParser;
using IniParser.Model;
using WheelWizard.Helpers;
using WheelWizard.Services;

namespace WheelWizard.Services.Input;

public static class MarioKartInputConfigService
{
    private const string GcPadConfigFileName = "GCPadNew.ini";
    private const string DolphinConfigFileName = "Dolphin.ini";
    private const string WiimoteConfigFileName = "WiimoteNew.ini";
    private const string ProfileFolderName = "Profiles";
    private const string GcPadProfileFolderName = "GCPad";
    private const string ActiveProfileFileName = "WheelWizard Active.ini";

    private const string LeftStickBinding = "left-stick";
    private const string RightStickBinding = "right-stick";
    private const string DPadBinding = "dpad";
    private const string FullDPadBinding = "full-dpad";
    private const string CustomDirectionalBindingPrefix = "custom|";
    private const string DefaultCalibration = "100.00";
    private const int DefaultDeadZonePercent = 0;
    private const string DefaultRumbleBinding = "`Motor` | `Motor L` | `Motor R` | `Strong` | `Weak`";

    private static readonly string[] GameCubeSections = ["GCPad1", "GCPad2", "GCPad3", "GCPad4"];
    private static readonly string[] WiimoteSections = ["Wiimote1", "Wiimote2", "Wiimote3", "Wiimote4"];
    private static readonly string[] ProfileValueKeys =
    [
        "Device",
        "Buttons/A",
        "Buttons/B",
        "Buttons/X",
        "Buttons/Z",
        "Buttons/Start",
        "Triggers/L",
        "Triggers/L-Analog",
        "Triggers/R",
        "Triggers/R-Analog",
        "Main Stick/Up",
        "Main Stick/Down",
        "Main Stick/Left",
        "Main Stick/Right",
        "D-Pad/Up",
        "D-Pad/Down",
        "D-Pad/Left",
        "D-Pad/Right",
        "Rumble/Motor",
    ];
    private static readonly object IniFileLock = new();

    public static MarioKartInputProfile LoadProfile()
    {
        var padConfigPath = GetConfigPath(GcPadConfigFileName);
        var padData = ReadIniFile(padConfigPath);
        var sourceSection = FindActivePadSection(padData);
        var section = EnsureSection(padData, sourceSection);

        if (!HasAnyProfileValue(section) && TryLoadSavedProfile(GetActiveProfilePath(), out var savedProfile))
            return savedProfile;

        return LoadProfileFromSection(section, sourceSection);
    }

    public static MarioKartInputProfile LoadProfile(MarioKartInputPresetOption? preset)
    {
        if (preset?.Kind == MarioKartInputPresetKind.SavedDolphinProfile)
        {
            return !string.IsNullOrWhiteSpace(preset.FilePath) && TryLoadSavedProfile(preset.FilePath, out var profile)
                ? profile
                : new MarioKartInputProfile { SourceSection = "Profile" };
        }

        return LoadProfile();
    }

    public static IReadOnlyList<MarioKartInputPresetOption> GetPresetOptions()
    {
        var options = new List<MarioKartInputPresetOption>
        {
            new(
                MarioKartInputPresetKind.CurrentDolphinSettings,
                "Current Dolphin Settings",
                "Use the GameCube controls Dolphin already has configured.",
                null
            ),
        };

        var profilesFolder = GetProfilesFolderPath();
        if (!Directory.Exists(profilesFolder))
            return options;

        try
        {
            options.AddRange(
                Directory
                    .EnumerateFiles(profilesFolder, "*.ini", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path =>
                        string.Equals(Path.GetFileName(path), ActiveProfileFileName, StringComparison.OrdinalIgnoreCase)
                    )
                    .ThenBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                    .Select(path =>
                    {
                        var profileName = Path.GetFileNameWithoutExtension(path);
                        var isWheelWizardActive = string.Equals(
                            Path.GetFileName(path),
                            ActiveProfileFileName,
                            StringComparison.OrdinalIgnoreCase
                        );

                        return new MarioKartInputPresetOption(
                            MarioKartInputPresetKind.SavedDolphinProfile,
                            isWheelWizardActive ? "Wheel Wizard Active" : profileName,
                            isWheelWizardActive ? "Last layout saved by Wheel Wizard." : "Saved Dolphin controller profile.",
                            path
                        );
                    })
            );
        }
        catch
        {
            // If Dolphin's profile folder is temporarily unavailable, the current settings option still works.
        }

        return options;
    }

    public static string GetProfilesFolderPath() => Path.Combine(PathManager.ConfigFolderPath, ProfileFolderName, GcPadProfileFolderName);

    private static MarioKartInputProfile LoadProfileFromSection(KeyDataCollection section, string sourceSection)
    {
        var profile = new MarioKartInputProfile
        {
            SourceSection = sourceSection,
            DeviceExpression = GetValue(section, "Device"),
            MainStickDeadZonePercent = LoadDeadZonePercent(section, "Main Stick/Dead Zone"),
            MainStickCalibration = GetValue(section, "Main Stick/Calibration", DefaultCalibration),
            CStickCalibration = GetValue(section, "C-Stick/Calibration", DefaultCalibration),
            RumbleBinding = GetValue(section, "Rumble/Motor", DefaultRumbleBinding),
        };

        profile.Bindings[MarioKartInputAction.Steering] = LoadDirectionalBinding(section);
        profile.Bindings[MarioKartInputAction.Accelerate] = GetValue(section, "Buttons/A");
        profile.Bindings[MarioKartInputAction.BrakeReverse] = GetValue(section, "Buttons/B");
        profile.Bindings[MarioKartInputAction.UseItem] = CombineDistinctBindings(
            GetValue(section, "Triggers/L"),
            GetValue(section, "Triggers/L-Analog")
        );
        profile.Bindings[MarioKartInputAction.Drift] = CombineDistinctBindings(
            GetValue(section, "Triggers/R"),
            GetValue(section, "Triggers/R-Analog")
        );
        profile.Bindings[MarioKartInputAction.LookBehind] = CombineDistinctBindings(
            GetValue(section, "Buttons/X"),
            GetValue(section, "Buttons/Z")
        );
        profile.Bindings[MarioKartInputAction.TrickWheelie] = LoadTrickWheelieBinding(section);
        profile.Bindings[MarioKartInputAction.Pause] = GetValue(section, "Buttons/Start");

        return profile;
    }

    public static void SaveProfile(MarioKartInputProfile profile)
    {
        FileHelper.EnsureDirectory(PathManager.ConfigFolderPath);

        var padConfigPath = GetConfigPath(GcPadConfigFileName);
        var padData = ReadIniFile(padConfigPath);
        var section = EnsureSection(padData, "GCPad1");

        section["Device"] = profile.DeviceExpression;
        section["Main Stick/Dead Zone"] = profile.MainStickDeadZonePercent.ToString(CultureInfo.InvariantCulture);
        section["Main Stick/Calibration"] = string.IsNullOrWhiteSpace(profile.MainStickCalibration)
            ? DefaultCalibration
            : profile.MainStickCalibration;
        section["C-Stick/Calibration"] = string.IsNullOrWhiteSpace(profile.CStickCalibration)
            ? DefaultCalibration
            : profile.CStickCalibration;
        section["Rumble/Motor"] = profile.RumbleBinding;

        WriteDirectionalBinding(section, profile.Bindings.GetValueOrDefault(MarioKartInputAction.Steering, LeftStickBinding));
        section["Buttons/A"] = profile.Bindings.GetValueOrDefault(MarioKartInputAction.Accelerate, string.Empty);
        section["Buttons/B"] = profile.Bindings.GetValueOrDefault(MarioKartInputAction.BrakeReverse, string.Empty);
        WriteMirroredBinding(
            section,
            "Triggers/L",
            "Triggers/L-Analog",
            profile.Bindings.GetValueOrDefault(MarioKartInputAction.UseItem, string.Empty)
        );
        WriteMirroredBinding(
            section,
            "Triggers/R",
            "Triggers/R-Analog",
            profile.Bindings.GetValueOrDefault(MarioKartInputAction.Drift, string.Empty)
        );
        WriteMirroredBinding(
            section,
            "Buttons/X",
            "Buttons/Z",
            profile.Bindings.GetValueOrDefault(MarioKartInputAction.LookBehind, string.Empty)
        );
        profile.Bindings[MarioKartInputAction.TrickWheelie] = NormalizeTrickWheelieBinding(
            profile.Bindings.GetValueOrDefault(MarioKartInputAction.TrickWheelie, string.Empty)
        );
        WriteTrickWheelieBinding(section, profile.Bindings[MarioKartInputAction.TrickWheelie]);
        section["Buttons/Start"] = profile.Bindings.GetValueOrDefault(MarioKartInputAction.Pause, "Start");

        WriteIniFile(padConfigPath, padData);
        SaveProfilePreset(profile);
        EnforceSingleGameCubeController();
        DisableAllWiimotes();
        profile.SourceSection = "GCPad1";
    }

    public static void EnsureLaunchProfileIsApplied()
    {
        if (!TryLoadLaunchProfile(out var profile))
            return;

        SaveProfile(profile);
    }

    public static MarioKartInputProfile CreateAutoMappedProfile(ControllerDeviceOption controller, MarioKartInputProfile currentProfile)
    {
        var autoMappedProfile = currentProfile.Clone();
        autoMappedProfile.DeviceExpression = controller.DeviceExpression;

        if (controller.IsKeyboard)
        {
            autoMappedProfile.Bindings[MarioKartInputAction.Steering] = CreateDirectionalBinding(
                MarioKartInputAction.Steering,
                new DirectionalBindingSet
                {
                    Up = KeyboardInputService.CreateAbsoluteBinding("UP"),
                    Down = KeyboardInputService.CreateAbsoluteBinding("DOWN"),
                    Left = KeyboardInputService.CreateAbsoluteBinding("LEFT"),
                    Right = KeyboardInputService.CreateAbsoluteBinding("RIGHT"),
                }
            );
            autoMappedProfile.Bindings[MarioKartInputAction.Accelerate] = KeyboardInputService.CreateAbsoluteBinding("X");
            autoMappedProfile.Bindings[MarioKartInputAction.BrakeReverse] = KeyboardInputService.CreateAbsoluteBinding("Z");
            autoMappedProfile.Bindings[MarioKartInputAction.UseItem] = KeyboardInputService.CreateAbsoluteBinding("C");
            autoMappedProfile.Bindings[MarioKartInputAction.Drift] = KeyboardInputService.CreateAbsoluteBinding("LSHIFT");
            autoMappedProfile.Bindings[MarioKartInputAction.LookBehind] = KeyboardInputService.CreateAbsoluteBinding("Q");
            autoMappedProfile.Bindings[MarioKartInputAction.TrickWheelie] = CreateDirectionalBinding(
                MarioKartInputAction.TrickWheelie,
                new DirectionalBindingSet
                {
                    Up = KeyboardInputService.CreateAbsoluteBinding("W"),
                    Down = KeyboardInputService.CreateAbsoluteBinding("S"),
                    Left = KeyboardInputService.CreateAbsoluteBinding("A"),
                    Right = KeyboardInputService.CreateAbsoluteBinding("D"),
                }
            );
            autoMappedProfile.Bindings[MarioKartInputAction.Pause] = KeyboardInputService.CreateAbsoluteBinding("RETURN");
            return autoMappedProfile;
        }

        autoMappedProfile.Bindings[MarioKartInputAction.Steering] = LeftStickBinding;
        autoMappedProfile.Bindings[MarioKartInputAction.Accelerate] = WrapToken("Button A");
        autoMappedProfile.Bindings[MarioKartInputAction.BrakeReverse] = WrapToken("Button B");
        autoMappedProfile.Bindings[MarioKartInputAction.UseItem] = WrapToken("Trigger L");
        autoMappedProfile.Bindings[MarioKartInputAction.Drift] = WrapToken("Trigger R");
        autoMappedProfile.Bindings[MarioKartInputAction.LookBehind] = CombineDistinctBindings(
            WrapToken("Button X"),
            WrapToken("Shoulder R")
        );
        autoMappedProfile.Bindings[MarioKartInputAction.TrickWheelie] = FullDPadBinding;
        autoMappedProfile.Bindings[MarioKartInputAction.Pause] = "Start";
        return autoMappedProfile;
    }

    public static bool IsRumbleEnabled(MarioKartInputProfile profile) => !string.IsNullOrWhiteSpace(profile.RumbleBinding);

    public static void SetRumbleEnabled(MarioKartInputProfile profile, bool enabled)
    {
        profile.RumbleBinding = enabled
            ? string.IsNullOrWhiteSpace(profile.RumbleBinding)
                ? DefaultRumbleBinding
                : profile.RumbleBinding
            : string.Empty;
    }

    public static string DescribeBinding(MarioKartInputAction action, string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
            return "Not set";

        if (action == MarioKartInputAction.Steering)
            return IsCustomDirectionalBinding(binding) ? "Custom" : DescribeDirectionalBinding(binding);

        if (action == MarioKartInputAction.TrickWheelie && binding == FullDPadBinding)
            return "D-Pad";

        if (action == MarioKartInputAction.TrickWheelie && IsCustomDirectionalBinding(binding))
            return "Custom";

        var rawTokens = SplitBinding(binding).Where(token => !string.IsNullOrWhiteSpace(token)).ToList();
        if (action == MarioKartInputAction.LookBehind && rawTokens.Count > 1)
            return "Custom";

        var tokens = rawTokens.Select(HumanizeToken).Where(token => !string.IsNullOrWhiteSpace(token)).ToList();

        return tokens.Count == 0 ? "Not set" : string.Join(" or ", tokens);
    }

    public static bool SupportsStickSettings(MarioKartInputAction action, string binding) =>
        action == MarioKartInputAction.Steering && binding is LeftStickBinding or RightStickBinding;

    public static bool SupportsDirectionEditor(MarioKartInputAction action, string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
            return false;

        return action switch
        {
            MarioKartInputAction.Steering => binding == DPadBinding || IsCustomDirectionalBinding(binding),
            MarioKartInputAction.TrickWheelie => binding == FullDPadBinding || IsDPadToken(binding) || IsCustomDirectionalBinding(binding),
            _ => false,
        };
    }

    public static bool SupportsMultiBindingEditor(MarioKartInputAction action) =>
        action
            is MarioKartInputAction.Accelerate
                or MarioKartInputAction.BrakeReverse
                or MarioKartInputAction.UseItem
                or MarioKartInputAction.Drift
                or MarioKartInputAction.LookBehind
                or MarioKartInputAction.Pause;

    public static string GetStickBindingDisplayName(string binding)
    {
        return binding switch
        {
            LeftStickBinding => "Left Stick",
            RightStickBinding => "Right Stick",
            _ => "Stick",
        };
    }

    public static IReadOnlyList<string> GetBindingTokens(string binding) => SplitBinding(binding).ToList();

    public static string CreateCombinedBinding(params string[] rawBindings) => CombineDistinctBindings(rawBindings);

    public static string DescribeSingleInputBinding(string binding)
    {
        var firstToken = SplitBinding(binding).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstToken) ? "Not set" : HumanizeToken(firstToken);
    }

    public static DirectionalBindingSet GetDirectionalBindingSet(MarioKartInputAction action, string binding)
    {
        if (action == MarioKartInputAction.Steering)
            return GetSteeringDirectionalBindingSet(binding);

        if (action == MarioKartInputAction.TrickWheelie)
            return GetTrickWheelieDirectionalBindingSet(binding);

        return new();
    }

    public static string CreateDirectionalBinding(MarioKartInputAction action, DirectionalBindingSet bindingSet)
    {
        if (action == MarioKartInputAction.Steering)
        {
            if (MatchesDefaultDirectionalPad(bindingSet))
                return DPadBinding;

            return BuildCustomDirectionalBinding(bindingSet);
        }

        if (action == MarioKartInputAction.TrickWheelie)
        {
            if (MatchesDefaultDirectionalPad(bindingSet))
                return FullDPadBinding;

            return BuildCustomDirectionalBinding(bindingSet);
        }

        return string.Empty;
    }

    public static string GetSavedDeviceDisplayName(string deviceExpression)
    {
        if (string.IsNullOrWhiteSpace(deviceExpression))
            return "No controller selected";

        if (KeyboardInputService.IsKeyboardDevice(deviceExpression))
            return "Keyboard";

        var segments = deviceExpression.Split('/');
        return segments.Length >= 3 ? string.Join("/", segments.Skip(2)) : deviceExpression;
    }

    public static string GetSavedDeviceSubtitle(string deviceExpression)
    {
        if (string.IsNullOrWhiteSpace(deviceExpression))
            return "Choose a controller to start mapping your controls.";

        if (KeyboardInputService.IsKeyboardDevice(deviceExpression))
            return "Saved keyboard mapping, ready to use on launch.";

        return "Saved in Dolphin and ready to use on launch.";
    }

    public static string WrapToken(string token) => token.Contains(' ') ? $"`{token}`" : token;

    private static string LoadDirectionalBinding(KeyDataCollection section)
    {
        var up = GetValue(section, "Main Stick/Up");
        var down = GetValue(section, "Main Stick/Down");
        var left = GetValue(section, "Main Stick/Left");
        var right = GetValue(section, "Main Stick/Right");

        if (up == WrapToken("Left Y+") && down == WrapToken("Left Y-") && left == WrapToken("Left X-") && right == WrapToken("Left X+"))
            return LeftStickBinding;

        if (up == WrapToken("Right Y+") && down == WrapToken("Right Y-") && left == WrapToken("Right X-") && right == WrapToken("Right X+"))
            return RightStickBinding;

        if (up == WrapToken("Pad N") && down == WrapToken("Pad S") && left == WrapToken("Pad W") && right == WrapToken("Pad E"))
            return DPadBinding;

        return BuildCustomDirectionalBinding(up, down, left, right);
    }

    private static string LoadTrickWheelieBinding(KeyDataCollection section)
    {
        var up = GetValue(section, "D-Pad/Up");
        var down = GetValue(section, "D-Pad/Down");
        var left = GetValue(section, "D-Pad/Left");
        var right = GetValue(section, "D-Pad/Right");

        if (MatchesDefaultDirectionalPad(up, down, left, right))
            return FullDPadBinding;

        if (!string.IsNullOrWhiteSpace(down) || !string.IsNullOrWhiteSpace(left) || !string.IsNullOrWhiteSpace(right))
            return BuildCustomDirectionalBinding(up, down, left, right);

        return up;
    }

    private static void WriteDirectionalBinding(KeyDataCollection section, string binding)
    {
        if (binding == LeftStickBinding)
        {
            section["Main Stick/Up"] = WrapToken("Left Y+");
            section["Main Stick/Down"] = WrapToken("Left Y-");
            section["Main Stick/Left"] = WrapToken("Left X-");
            section["Main Stick/Right"] = WrapToken("Left X+");
            return;
        }

        if (binding == RightStickBinding)
        {
            section["Main Stick/Up"] = WrapToken("Right Y+");
            section["Main Stick/Down"] = WrapToken("Right Y-");
            section["Main Stick/Left"] = WrapToken("Right X-");
            section["Main Stick/Right"] = WrapToken("Right X+");
            return;
        }

        if (binding == DPadBinding)
        {
            section["Main Stick/Up"] = WrapToken("Pad N");
            section["Main Stick/Down"] = WrapToken("Pad S");
            section["Main Stick/Left"] = WrapToken("Pad W");
            section["Main Stick/Right"] = WrapToken("Pad E");
            return;
        }

        var values = ParseDirectionalBindingValues(binding);

        section["Main Stick/Up"] = values.GetValueOrDefault("up", string.Empty);
        section["Main Stick/Down"] = values.GetValueOrDefault("down", string.Empty);
        section["Main Stick/Left"] = values.GetValueOrDefault("left", string.Empty);
        section["Main Stick/Right"] = values.GetValueOrDefault("right", string.Empty);
    }

    private static void WriteTrickWheelieBinding(KeyDataCollection section, string binding)
    {
        var normalizedBinding = NormalizeTrickWheelieBinding(binding);

        if (normalizedBinding == FullDPadBinding)
        {
            section["D-Pad/Up"] = WrapToken("Pad N");
            section["D-Pad/Down"] = WrapToken("Pad S");
            section["D-Pad/Left"] = WrapToken("Pad W");
            section["D-Pad/Right"] = WrapToken("Pad E");
            return;
        }

        if (IsCustomDirectionalBinding(normalizedBinding))
        {
            var customBinding = ParseCustomDirectionalBinding(normalizedBinding);
            section["D-Pad/Up"] = customBinding.Up;
            section["D-Pad/Down"] = customBinding.Down;
            section["D-Pad/Left"] = customBinding.Left;
            section["D-Pad/Right"] = customBinding.Right;
            return;
        }

        section["D-Pad/Up"] = normalizedBinding;
        section["D-Pad/Down"] = string.Empty;
        section["D-Pad/Left"] = string.Empty;
        section["D-Pad/Right"] = string.Empty;
    }

    private static void WriteMirroredBinding(KeyDataCollection section, string firstKey, string secondKey, string binding)
    {
        var normalizedBinding = NormalizeCombinedBinding(binding);
        section[firstKey] = normalizedBinding;
        section[secondKey] = normalizedBinding;
    }

    private static string CombineDistinctBindings(params string[] rawBindings)
    {
        var uniqueBindings = new List<string>();
        foreach (var binding in rawBindings)
        {
            foreach (var token in SplitBinding(binding))
            {
                if (!uniqueBindings.Contains(token, StringComparer.Ordinal))
                    uniqueBindings.Add(token);
            }
        }

        return string.Join(" | ", uniqueBindings);
    }

    private static string NormalizeCombinedBinding(string binding) => CombineDistinctBindings(binding);

    private static string NormalizeTrickWheelieBinding(string binding) => IsDPadToken(binding) ? FullDPadBinding : binding;

    private static bool IsCustomDirectionalBinding(string binding) =>
        binding.StartsWith(CustomDirectionalBindingPrefix, StringComparison.Ordinal);

    private static DirectionalBindingSet GetSteeringDirectionalBindingSet(string binding)
    {
        if (binding == DPadBinding)
            return GetDefaultDirectionalPad();

        return IsCustomDirectionalBinding(binding) ? ParseCustomDirectionalBinding(binding) : new();
    }

    private static DirectionalBindingSet GetTrickWheelieDirectionalBindingSet(string binding)
    {
        if (binding == FullDPadBinding || IsDPadToken(binding))
            return GetDefaultDirectionalPad();

        return IsCustomDirectionalBinding(binding) ? ParseCustomDirectionalBinding(binding) : new();
    }

    private static DirectionalBindingSet ParseCustomDirectionalBinding(string binding)
    {
        var values = ParseDirectionalBindingValues(binding);
        return new()
        {
            Up = values.GetValueOrDefault("up", string.Empty),
            Down = values.GetValueOrDefault("down", string.Empty),
            Left = values.GetValueOrDefault("left", string.Empty),
            Right = values.GetValueOrDefault("right", string.Empty),
        };
    }

    private static Dictionary<string, string> ParseDirectionalBindingValues(string binding)
    {
        var parts = binding.Split('|', StringSplitOptions.TrimEntries);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts.Skip(1))
        {
            var splitIndex = part.IndexOf('=');
            if (splitIndex <= 0 || splitIndex == part.Length - 1)
                continue;

            values[part[..splitIndex]] = DecodeDirectionalBindingValue(part[(splitIndex + 1)..]);
        }

        return values;
    }

    private static string BuildCustomDirectionalBinding(DirectionalBindingSet bindingSet) =>
        BuildCustomDirectionalBinding(bindingSet.Up, bindingSet.Down, bindingSet.Left, bindingSet.Right);

    private static string BuildCustomDirectionalBinding(string up, string down, string left, string right) =>
        $"{CustomDirectionalBindingPrefix}up={EncodeDirectionalBindingValue(up)}|down={EncodeDirectionalBindingValue(down)}|left={EncodeDirectionalBindingValue(left)}|right={EncodeDirectionalBindingValue(right)}";

    private static DirectionalBindingSet GetDefaultDirectionalPad() =>
        new()
        {
            Up = WrapToken("Pad N"),
            Down = WrapToken("Pad S"),
            Left = WrapToken("Pad W"),
            Right = WrapToken("Pad E"),
        };

    private static bool MatchesDefaultDirectionalPad(DirectionalBindingSet bindingSet) =>
        MatchesDefaultDirectionalPad(bindingSet.Up, bindingSet.Down, bindingSet.Left, bindingSet.Right);

    private static bool MatchesDefaultDirectionalPad(string up, string down, string left, string right) =>
        up == WrapToken("Pad N") && down == WrapToken("Pad S") && left == WrapToken("Pad W") && right == WrapToken("Pad E");

    private static bool IsDPadToken(string binding)
    {
        var normalizedBinding = binding.Trim();
        return normalizedBinding is "`Pad N`" or "`Pad S`" or "`Pad W`" or "`Pad E`";
    }

    private static IEnumerable<string> SplitBinding(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
            return [];

        var tokens = new List<string>();
        var startIndex = 0;
        var parenthesisDepth = 0;
        var inBackticks = false;

        for (var index = 0; index < binding.Length; index++)
        {
            var character = binding[index];

            if (character == '`')
            {
                inBackticks = !inBackticks;
                continue;
            }

            if (inBackticks)
                continue;

            switch (character)
            {
                case '(':
                    parenthesisDepth++;
                    break;
                case ')' when parenthesisDepth > 0:
                    parenthesisDepth--;
                    break;
                case '|' when parenthesisDepth == 0:
                    AddBindingToken(binding[startIndex..index]);
                    startIndex = index + 1;
                    break;
            }
        }

        AddBindingToken(binding[startIndex..]);
        return tokens;

        void AddBindingToken(string token)
        {
            var trimmedToken = token.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedToken))
                tokens.Add(trimmedToken);
        }
    }

    private static string DescribeDirectionalBinding(string binding)
    {
        return binding switch
        {
            LeftStickBinding => "Left Stick",
            RightStickBinding => "Right Stick",
            DPadBinding => "D-Pad",
            _ => DescribeCustomDirectionalBinding(binding),
        };
    }

    private static string DescribeCustomDirectionalBinding(string binding)
    {
        var parts = binding.Split('|', StringSplitOptions.TrimEntries);
        var directionLines = new List<string>();

        foreach (var part in parts.Skip(1))
        {
            var splitIndex = part.IndexOf('=');
            if (splitIndex <= 0 || splitIndex == part.Length - 1)
                continue;

            var direction = part[..splitIndex] switch
            {
                "up" => "Up",
                "down" => "Down",
                "left" => "Left",
                "right" => "Right",
                _ => part[..splitIndex],
            };

            directionLines.Add($"{direction}: {DescribeCombinedBindingValue(DecodeDirectionalBindingValue(part[(splitIndex + 1)..]))}");
        }

        return directionLines.Count == 0 ? "Custom" : string.Join("  ", directionLines);
    }

    private static string EncodeDirectionalBindingValue(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private static string DecodeDirectionalBindingValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            // Fall back to the raw value so older or malformed bindings stay editable.
            return value;
        }
    }

    private static string DescribeCombinedBindingValue(string binding)
    {
        var tokens = SplitBinding(binding).Select(HumanizeToken).Where(token => !string.IsNullOrWhiteSpace(token)).ToList();
        return tokens.Count == 0 ? "Not set" : string.Join(" or ", tokens);
    }

    private static string HumanizeToken(string token)
    {
        var cleanedToken = token.Trim().Trim('`');

        if (TryExtractKeyboardToken(cleanedToken, out var keyboardToken))
            return HumanizeKeyboardToken(keyboardToken);

        return cleanedToken switch
        {
            "Button A" => "Face Button Bottom",
            "Button B" => "Face Button Right",
            "Button X" => "Face Button Left",
            "Button Y" => "Face Button Top",
            "Shoulder L" => "Left Bumper",
            "Shoulder R" => "Right Bumper",
            "Trigger L" => "Left Trigger",
            "Trigger R" => "Right Trigger",
            "Pad N" => "D-Pad Up",
            "Pad S" => "D-Pad Down",
            "Pad W" => "D-Pad Left",
            "Pad E" => "D-Pad Right",
            "Start" => "Start",
            "Back" => "Back",
            "Left Stick" => "Left Stick Click",
            "Right Stick" => "Right Stick Click",
            "Left X+" or "Left X-" or "Left Y+" or "Left Y-" => "Left Stick",
            "Right X+" or "Right X-" or "Right Y+" or "Right Y-" => "Right Stick",
            "Motor" or "Motor L" or "Motor R" or "Strong" or "Weak" => "Rumble",
            _ => HumanizeKeyboardToken(cleanedToken),
        };
    }

    private static bool TryExtractKeyboardToken(string cleanedToken, out string keyboardToken)
    {
        keyboardToken = string.Empty;

        var separatorIndex = cleanedToken.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == cleanedToken.Length - 1)
            return false;

        if (!KeyboardInputService.IsKeyboardDevice(cleanedToken[..separatorIndex]))
            return false;

        keyboardToken = cleanedToken[(separatorIndex + 1)..];
        return true;
    }

    private static string HumanizeKeyboardToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return token;

        var upperToken = token.Trim().ToUpperInvariant();
        var compactToken = upperToken.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

        if (compactToken.Length == 1 || compactToken.StartsWith("F", StringComparison.Ordinal) && int.TryParse(compactToken[1..], out _))
        {
            return compactToken;
        }

        if (
            compactToken.StartsWith("NUMPAD", StringComparison.Ordinal)
            && int.TryParse(compactToken["NUMPAD".Length..], out var numpadDigit)
        )
            return $"Numpad {numpadDigit}";

        if (compactToken.StartsWith("KP", StringComparison.Ordinal) && int.TryParse(compactToken["KP".Length..], out var keypadDigit))
            return $"Numpad {keypadDigit}";

        return compactToken switch
        {
            "RETURN" => "Enter",
            "ENTER" => "Enter",
            "KPENTER" => "Numpad Enter",
            "SPACE" => "Space",
            "TAB" => "Tab",
            "ESCAPE" => "Escape",
            "ESC" => "Escape",
            "BACK" => "Backspace",
            "BACKSPACE" => "Backspace",
            "DELETE" => "Delete",
            "DEL" => "Delete",
            "INSERT" => "Insert",
            "INS" => "Insert",
            "HOME" => "Home",
            "END" => "End",
            "PRIOR" => "Page Up",
            "PAGEUP" => "Page Up",
            "NEXT" => "Page Down",
            "PAGEDOWN" => "Page Down",
            "CAPITAL" => "Caps Lock",
            "CAPSLOCK" => "Caps Lock",
            "NUMLOCK" => "Num Lock",
            "SCROLL" => "Scroll Lock",
            "SCROLLLOCK" => "Scroll Lock",
            "PAUSE" => "Pause",
            "SYSRQ" => "Print Screen",
            "PRINTSCREEN" => "Print Screen",
            "LEFT" => "Left Arrow",
            "RIGHT" => "Right Arrow",
            "UP" => "Up Arrow",
            "DOWN" => "Down Arrow",
            "LEFTARROW" => "Left Arrow",
            "RIGHTARROW" => "Right Arrow",
            "UPARROW" => "Up Arrow",
            "DOWNARROW" => "Down Arrow",
            "LSHIFT" => "Left Shift",
            "RSHIFT" => "Right Shift",
            "SHIFTLEFT" => "Left Shift",
            "SHIFTRIGHT" => "Right Shift",
            "LEFTSHIFT" => "Left Shift",
            "RIGHTSHIFT" => "Right Shift",
            "LCONTROL" => "Left Ctrl",
            "RCONTROL" => "Right Ctrl",
            "LCTRL" => "Left Ctrl",
            "RCTRL" => "Right Ctrl",
            "CONTROLLEFT" => "Left Ctrl",
            "CONTROLRIGHT" => "Right Ctrl",
            "LEFTCONTROL" => "Left Ctrl",
            "RIGHTCONTROL" => "Right Ctrl",
            "LMENU" => "Left Alt",
            "RMENU" => "Right Alt",
            "LALT" => "Left Alt",
            "RALT" => "Right Alt",
            "ALTLEFT" => "Left Alt",
            "ALTRIGHT" => "Right Alt",
            "LEFTALT" => "Left Alt",
            "RIGHTALT" => "Right Alt",
            "LWIN" => "Left Win",
            "RWIN" => "Right Win",
            "LSUPER" => "Left Win",
            "RSUPER" => "Right Win",
            "LEFTSUPER" => "Left Win",
            "RIGHTSUPER" => "Right Win",
            "APPS" => "Menu",
            "MULTIPLY" => "Numpad *",
            "KPMULTIPLY" => "Numpad *",
            "ADD" => "Numpad +",
            "KPPLUS" => "Numpad +",
            "SUBTRACT" => "Numpad -",
            "KPMINUS" => "Numpad -",
            "DECIMAL" => "Numpad .",
            "KPDECIMAL" => "Numpad .",
            "DIVIDE" => "Numpad /",
            "KPDIVIDE" => "Numpad /",
            "MINUS" => "-",
            "EQUALS" => "=",
            "COMMA" => ",",
            "PERIOD" => ".",
            "SEMICOLON" => ";",
            "SLASH" => "/",
            "GRAVE" => "`",
            "LBRACKET" => "[",
            "BACKSLASH" => "\\",
            "RBRACKET" => "]",
            "APOSTROPHE" => "'",
            _ => token,
        };
    }

    private static void SaveProfilePreset(MarioKartInputProfile profile)
    {
        var profileFolder = GetProfilesFolderPath();
        FileHelper.EnsureDirectory(profileFolder);
        var profilePath = GetActiveProfilePath();

        var profileData = new IniData();
        var profileSection = EnsureSection(profileData, "Profile");
        profileSection["Device"] = profile.DeviceExpression;
        profileSection["Main Stick/Dead Zone"] = profile.MainStickDeadZonePercent.ToString(CultureInfo.InvariantCulture);
        profileSection["Buttons/A"] = profile.Bindings.GetValueOrDefault(MarioKartInputAction.Accelerate, string.Empty);
        profileSection["Buttons/B"] = profile.Bindings.GetValueOrDefault(MarioKartInputAction.BrakeReverse, string.Empty);
        profileSection["Buttons/Start"] = profile.Bindings.GetValueOrDefault(MarioKartInputAction.Pause, "Start");
        WriteTrickWheelieBinding(profileSection, profile.Bindings.GetValueOrDefault(MarioKartInputAction.TrickWheelie, string.Empty));
        profileSection["Rumble/Motor"] = profile.RumbleBinding;
        profileSection["Main Stick/Calibration"] = string.IsNullOrWhiteSpace(profile.MainStickCalibration)
            ? DefaultCalibration
            : profile.MainStickCalibration;
        profileSection["C-Stick/Calibration"] = string.IsNullOrWhiteSpace(profile.CStickCalibration)
            ? DefaultCalibration
            : profile.CStickCalibration;

        WriteDirectionalBinding(profileSection, profile.Bindings.GetValueOrDefault(MarioKartInputAction.Steering, LeftStickBinding));
        WriteMirroredBinding(
            profileSection,
            "Triggers/L",
            "Triggers/L-Analog",
            profile.Bindings.GetValueOrDefault(MarioKartInputAction.UseItem, string.Empty)
        );
        WriteMirroredBinding(
            profileSection,
            "Triggers/R",
            "Triggers/R-Analog",
            profile.Bindings.GetValueOrDefault(MarioKartInputAction.Drift, string.Empty)
        );
        WriteMirroredBinding(
            profileSection,
            "Buttons/X",
            "Buttons/Z",
            profile.Bindings.GetValueOrDefault(MarioKartInputAction.LookBehind, string.Empty)
        );

        WriteIniFile(profilePath, profileData);
    }

    private static bool TryLoadLaunchProfile(out MarioKartInputProfile profile)
    {
        if (TryLoadCurrentProfile(out profile) && !string.IsNullOrWhiteSpace(profile.DeviceExpression))
            return true;

        return TryLoadSavedProfile(GetActiveProfilePath(), out profile) && !string.IsNullOrWhiteSpace(profile.DeviceExpression);
    }

    private static bool TryLoadCurrentProfile(out MarioKartInputProfile profile)
    {
        profile = new();

        var padConfigPath = GetConfigPath(GcPadConfigFileName);
        if (!FileHelper.FileExists(padConfigPath))
            return false;

        var padData = ReadIniFile(padConfigPath);
        var sourceSection = FindActivePadSection(padData);
        if (!padData.Sections.ContainsSection(sourceSection))
            return false;

        var section = padData[sourceSection];
        if (!HasAnyProfileValue(section))
            return false;

        profile = LoadProfileFromSection(section, sourceSection);
        return true;
    }

    private static bool TryLoadSavedProfile(string profilePath, out MarioKartInputProfile profile)
    {
        profile = new();

        if (string.IsNullOrWhiteSpace(profilePath) || !FileHelper.FileExists(profilePath))
            return false;

        var profileData = ReadIniFile(profilePath);
        if (!profileData.Sections.ContainsSection("Profile"))
            return false;

        var profileSection = profileData["Profile"];
        if (!HasAnyProfileValue(profileSection))
            return false;

        profile = LoadProfileFromSection(profileSection, "Profile");
        return true;
    }

    private static void EnforceSingleGameCubeController()
    {
        var dolphinConfigPath = GetConfigPath(DolphinConfigFileName);
        var dolphinData = ReadIniFile(dolphinConfigPath);
        var coreSection = EnsureSection(dolphinData, "Core");
        coreSection["SIDevice0"] = "6";
        coreSection["SIDevice1"] = "0";
        coreSection["SIDevice2"] = "0";
        coreSection["SIDevice3"] = "0";
        WriteIniFile(dolphinConfigPath, dolphinData);
    }

    private static void DisableAllWiimotes()
    {
        var wiimoteConfigPath = GetConfigPath(WiimoteConfigFileName);
        var wiimoteData = ReadIniFile(wiimoteConfigPath);

        foreach (var wiimoteSectionName in WiimoteSections)
        {
            var wiimoteSection = EnsureSection(wiimoteData, wiimoteSectionName);
            wiimoteSection["Device"] = "Disabled//";
            wiimoteSection["Source"] = "0";
        }

        WriteIniFile(wiimoteConfigPath, wiimoteData);
    }

    private static string FindActivePadSection(IniData data)
    {
        foreach (var sectionName in GameCubeSections)
        {
            if (!data.Sections.ContainsSection(sectionName))
                continue;

            var device = GetValue(data[sectionName], "Device");
            if (!string.IsNullOrWhiteSpace(device) && !device.Equals("Disabled//", StringComparison.OrdinalIgnoreCase))
                return sectionName;
        }

        return "GCPad1";
    }

    private static bool HasAnyProfileValue(KeyDataCollection section) =>
        ProfileValueKeys.Any(key => !string.IsNullOrWhiteSpace(GetValue(section, key)));

    private static string GetValue(KeyDataCollection section, string key, string fallback = "")
    {
        if (!section.ContainsKey(key))
            return fallback;

        return section[key] ?? fallback;
    }

    private static int LoadDeadZonePercent(KeyDataCollection section, string key)
    {
        var rawValue = GetValue(section, key);
        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return DefaultDeadZonePercent;

        return Math.Clamp((int)Math.Round(percent), 0, 95);
    }

    private static string GetConfigPath(string fileName) => Path.Combine(PathManager.ConfigFolderPath, fileName);

    private static string GetActiveProfilePath() => Path.Combine(GetProfilesFolderPath(), ActiveProfileFileName);

    private static IniData ReadIniFile(string path)
    {
        lock (IniFileLock)
        {
            if (!FileHelper.FileExists(path))
                return new IniData();

            try
            {
                var parser = new FileIniDataParser();
                return parser.ReadFile(path);
            }
            catch
            {
                TryBackUpUnreadableIni(path);
                return new IniData();
            }
        }
    }

    private static void WriteIniFile(string path, IniData data)
    {
        lock (IniFileLock)
        {
            FileHelper.EnsureDirectory(Path.GetDirectoryName(path) ?? PathManager.ConfigFolderPath);
            var parser = new FileIniDataParser();
            parser.WriteFile(path, data);
        }
    }

    private static void TryBackUpUnreadableIni(string path)
    {
        try
        {
            if (!FileHelper.FileExists(path))
                return;

            var backupPath = $"{path}.WheelWizard.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: false);
        }
        catch
        {
            // Best effort only. Loading a clean config is safer than crashing the input page.
        }
    }

    private static KeyDataCollection EnsureSection(IniData data, string sectionName)
    {
        if (!data.Sections.ContainsSection(sectionName))
            data.Sections.AddSection(sectionName);

        return data[sectionName];
    }
}
