using System.IO.Abstractions;
using System.Text.RegularExpressions;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Dolphin.Paths;

/// <summary>Resolves one explicit Dolphin command/user-directory pair without reading settings.</summary>
public sealed partial class DolphinPathLayout(
    IPath path,
    IRuntimeEnvironment environment,
    bool isFlatpakSandboxed,
    string dolphinLocation,
    string userFolderPath
)
{
    private IPath Path => path;
    private string HomeFolderPath => environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
    private string AppDataFolder => environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
    private string LocalAppDataFolder => environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
    public string DolphinFilePath => isFlatpakSandboxed ? "/app/bin/dolphin-emu-wrapper" : dolphinLocation;
    public string UserFolderPath => userFolderPath;
    private string LinuxDolphinLegacyRelSubFolderPath => ".dolphin-emu";
    public string LinuxDolphinLegacyFolderPath => Path.Combine(HomeFolderPath, LinuxDolphinLegacyRelSubFolderPath);
    private string LinuxDolphinRelSubFolderPath => "dolphin-emu";

    // We at least try to be compatible with potential forks of Dolphin (different app IDs) but default to the original Dolphin Flatpak
    internal string LinuxDolphinFlatpakAppDataFolderPath =>
        Path.Combine(
            HomeFolderPath,
            ".var",
            "app",
            IsFlatpakSandboxed()
                ? ExtractDolphinFlatpakAppIdOverrideFromUserFolder(UserFolderPath)
                : ExtractDolphinFlatpakAppId(DolphinFilePath)
        );
    public string LinuxDolphinFlatpakDataDir => Path.Combine(LinuxDolphinFlatpakAppDataFolderPath, "data", LinuxDolphinRelSubFolderPath);
    public string LinuxDolphinFlatpakConfigDir =>
        Path.Combine(LinuxDolphinFlatpakAppDataFolderPath, "config", LinuxDolphinRelSubFolderPath);

    private string? NullIfRelativeLinuxPath(string path)
    {
        return path.StartsWith('/') ? path : null;
    }

    private bool IsFlatpakSandboxed()
    {
        return isFlatpakSandboxed;
    }

    public string LinuxXdgDataHome => LocalAppDataFolder;
    public string LinuxXdgConfigHome => AppDataFolder;
    internal string LinuxHostXdgDataHome =>
        NullIfRelativeLinuxPath(environment.GetEnvironmentVariable("HOST_XDG_DATA_HOME") ?? string.Empty)
        ?? Path.Combine(HomeFolderPath, ".local", "share");
    internal string LinuxHostXdgConfigHome =>
        NullIfRelativeLinuxPath(environment.GetEnvironmentVariable("HOST_XDG_CONFIG_HOME") ?? string.Empty)
        ?? Path.Combine(HomeFolderPath, ".config");

    internal string LinuxDolphinHostNativeInstallConfigDir => Path.Combine(LinuxHostXdgConfigHome, LinuxDolphinRelSubFolderPath);
    internal string LinuxDolphinHostNativeInstallDataDir => Path.Combine(LinuxHostXdgDataHome, LinuxDolphinRelSubFolderPath);
    public string LinuxDolphinNativeInstallConfigDir => Path.Combine(LinuxXdgConfigHome, LinuxDolphinRelSubFolderPath);
    public string LinuxDolphinNativeInstallDataDir => Path.Combine(LinuxXdgDataHome, LinuxDolphinRelSubFolderPath);

    private string LinuxFlatpakBundledDolphinXdgInternalSuffix => "-dolphin-emu";

    public string LinuxFlatpakBundledDolphinXdgConfigDir =>
        Path.Combine(
            HomeFolderPath,
            ".var",
            "app",
            WheelWizardFlatpakAppId,
            "config" + LinuxFlatpakBundledDolphinXdgInternalSuffix,
            LinuxDolphinRelSubFolderPath
        );

    public string LinuxFlatpakBundledDolphinXdgDataDir =>
        Path.Combine(
            HomeFolderPath,
            ".var",
            "app",
            WheelWizardFlatpakAppId,
            "data" + LinuxFlatpakBundledDolphinXdgInternalSuffix,
            LinuxDolphinRelSubFolderPath
        );

    public string[] LinuxFlatpakSandboxedDolphinUserFolderBlockList =>
        [LinuxFlatpakBundledDolphinXdgConfigDir, LinuxFlatpakBundledDolphinXdgDataDir];

    public string SplitLinuxDolphinNativeConfigDir
    {
        get
        {
            if (IsFlatpakSandboxed())
            {
                if (LinuxDolphinHostNativeInstallDataDir.Equals(Path.GetFullPath(UserFolderPath), StringComparison.Ordinal))
                    return LinuxDolphinHostNativeInstallConfigDir;
            }
            else if (LinuxDolphinNativeInstallDataDir.Equals(Path.GetFullPath(UserFolderPath), StringComparison.Ordinal))
            {
                return LinuxDolphinNativeInstallConfigDir;
            }

            return string.Empty;
        }
    }

    public string SplitLinuxDolphinConfigDir
    {
        get
        {
            if (IsFlatpakSandboxed() || IsFlatpakDolphinFilePath(DolphinFilePath))
            {
                if (LinuxDolphinFlatpakDataDir.Equals(Path.GetFullPath(UserFolderPath), StringComparison.Ordinal))
                    return LinuxDolphinFlatpakConfigDir;
            }

            if (!IsFlatpakSandboxed() && IsFlatpakDolphinFilePath(DolphinFilePath))
            {
                // Early return in the case of non-sandboxed Wheel Wizard: the Dolphin executable/command governs the decision
                return string.Empty;
            }
            else
            {
                // Flatpak-sandboxed Wheel Wizard will also consider the split native config directory based on the user folder
                // since the executable/command for Dolphin is not selectable anymore
                return SplitLinuxDolphinNativeConfigDir;
            }
        }
    }

    public bool IsLinuxDolphinConfigSplit()
    {
        // Our Flatpak will always use split config/data directories internally.
        return IsFlatpakSandboxed() || !string.IsNullOrWhiteSpace(SplitLinuxDolphinConfigDir);
    }

    public string ConfigFolderPath
    {
        get
        {
            if (environment.IsLinux)
            {
                try
                {
                    var determinedLinuxDolphinConfigDir = SplitLinuxDolphinConfigDir;
                    if (!string.IsNullOrWhiteSpace(determinedLinuxDolphinConfigDir))
                        return determinedLinuxDolphinConfigDir;
                }
                catch
                {
                    // Fall back to something that is likely not valid, will be checked later
                    return Path.Combine(UserFolderPath, "Config");
                }
            }
            return Path.Combine(UserFolderPath, "Config");
        }
    }

    public bool IsFlatpakDolphinFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            // Prioritize Flatpak Dolphin installation if no file path has been saved yet, so return true
            return true;
        }
        // Because we need this prefix for the permission workarounds, we just expect it to start with "flatpak run"
        var flatpakRunCommand = "flatpak run";
        return filePath.StartsWith(flatpakRunCommand, StringComparison.Ordinal);
    }

    public string WheelWizardFlatpakAppId => environment.GetEnvironmentVariable("FLATPAK_ID") ?? "io.github.TeamWheelWizard.WheelWizard";

    public const string DefaultDolphinFlatpakAppId = "org.DolphinEmu.dolphin-emu";

    [GeneratedRegex(@"(?i)\b[a-z][a-z0-9]*(?:\.[a-z_][a-z0-9_]*){1,}\.[a-z_][a-z0-9_-]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex FlatpakRunAppIdRegex { get; }

    /// <summary>
    /// Pulls the app ID out of a "flatpak run ..." command, so custom or forked Dolphin Flatpaks keep working.
    /// </summary>
    public string ExtractDolphinFlatpakAppId(string flatpakDolphinLocation)
    {
        if (string.IsNullOrWhiteSpace(flatpakDolphinLocation))
            return DefaultDolphinFlatpakAppId;

        var matches = FlatpakRunAppIdRegex.Matches(flatpakDolphinLocation);
        return matches.Count == 0 ? DefaultDolphinFlatpakAppId : matches[^1].Value;
    }

    [GeneratedRegex(
        @"(?i)^\.var/app/(?<AppId>[a-z][a-z0-9]*(?:\.[a-z_][a-z0-9_]*){1,}\.[a-z_][a-z0-9_-]*)/data/dolphin-emu/?$",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex DolphinFlatpakAppIdInUserFolderRegex { get; }

    /// <summary>
    /// Extracts a potential Dolphin Flatpak app ID from a configured
    /// Dolphin user folder if it matches a <c>dolphin-emu</c> data folder
    /// in <c>~/.var/app/[appId]</c>.
    /// </summary>
    private string ExtractDolphinFlatpakAppIdOverrideFromUserFolder(string flatpakUserFolder)
    {
        if (!IsFlatpakSandboxed() || string.IsNullOrWhiteSpace(flatpakUserFolder))
        {
            return DefaultDolphinFlatpakAppId;
        }

        var fullFlatpakUserFolderPath = NormalizePath(flatpakUserFolder);
        var fullHomePath = NormalizePath(HomeFolderPath);

        if (!fullFlatpakUserFolderPath.StartsWith(fullHomePath + '/', StringComparison.Ordinal))
        {
            return DefaultDolphinFlatpakAppId;
        }

        var homeDirRelativeFlatpakUserFolderPath = Path.GetRelativePath(fullHomePath, fullFlatpakUserFolderPath);

        var match = DolphinFlatpakAppIdInUserFolderRegex.Match(homeDirRelativeFlatpakUserFolderPath);

        return match.Success ? match.Groups["AppId"].Value : DefaultDolphinFlatpakAppId;
    }

    private string GetContainingBaseDirectorySafe(string path)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public string GetDolphinExeDirectory()
    {
        return GetContainingBaseDirectorySafe(DolphinFilePath);
    }

    private string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path cannot be empty.", nameof(value));
        var fullPath = Path.GetFullPath(value);
        var root = Path.GetPathRoot(fullPath) ?? "";
        while (fullPath.Length > root.Length)
        {
            var trimmed = Path.TrimEndingDirectorySeparator(fullPath);
            if (trimmed.Equals(fullPath, StringComparison.Ordinal))
                break;
            fullPath = trimmed;
        }
        return fullPath;
    }
}
