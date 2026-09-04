using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// Builds the command lines described by the WheelWizard-Recomp integration contract.
/// The recomp owns the actual behaviour; WheelWizard only decides which of its own paths to hand over.
/// The Windows host takes <c>--verb</c> flags, the Linux host takes the same verbs as subcommands.
/// </summary>
public static class RecompSetupCommandBuilder
{
    /// <summary>
    /// Builds the arguments for a silent install, which doubles as the in-place upgrade command.
    /// </summary>
    public static string BuildSilentInstallArguments(RecompHostPlatform platform, RecompInstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GameFilePath))
            throw new ArgumentException("A game image path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.InstallFolderPath))
            throw new ArgumentException("An install directory is required.", nameof(request));

        var arguments = new List<string>
        {
            platform == RecompHostPlatform.Windows ? "--silent" : "install",
            "--game",
            Quote(request.GameFilePath),
            "--install-dir",
            Quote(request.InstallFolderPath),
        };

        // Only a silent install may request the portable layout, and only when WheelWizard actually
        // handed over its own portable location. An installation that predates portable mode keeps
        // its machine-local layout for the rest of its life.
        if (request.Portable)
            arguments.Add("--portable");

        arguments.Add("--progress-json");

        if (!string.IsNullOrWhiteSpace(request.RetroRewindFolderPath))
        {
            arguments.Add("--retro-dir");
            arguments.Add(Quote(request.RetroRewindFolderPath));
            arguments.Add(RetroWfcPayloadArgument);
        }

        return string.Join(' ', arguments);
    }

    /// <summary>
    /// Builds a repair that performs only the work the installed products actually need, using the
    /// toolkit the installation already owns instead of an embedded payload. Every repair receives the
    /// Retro Rewind source and exactly one Retro-WFC payload option, as the contract requires.
    /// Windows only: the Linux host repairs by running <c>install</c> again.
    /// </summary>
    public static string BuildRepairProductsArguments(string installFolderPath, string retroRewindFolderPath)
    {
        if (string.IsNullOrWhiteSpace(installFolderPath))
            throw new ArgumentException("An install directory is required.", nameof(installFolderPath));
        if (string.IsNullOrWhiteSpace(retroRewindFolderPath))
            throw new ArgumentException("A Retro Rewind source directory is required.", nameof(retroRewindFolderPath));

        return string.Join(
            ' ',
            "--repair-products",
            "--install-dir",
            Quote(installFolderPath),
            "--retro-dir",
            Quote(retroRewindFolderPath),
            RetroWfcPayloadArgument,
            "--progress-json"
        );
    }

    /// <summary>
    /// Builds the arguments used to start the game. WheelWizard is the Retro Rewind frontend, so it starts
    /// Retro Rewind whenever that product is installed and only falls back to the unmodded game otherwise.
    /// </summary>
    public static string BuildLaunchArguments(RecompHostPlatform platform, bool retroRewind) =>
        Verb(platform, retroRewind ? "launch-retro" : "launch-base");

    /// <summary>
    /// Builds the arguments that report, without building anything, whether the installed executables are
    /// still the output of the installed toolkit and the installed <c>Code.pul</c>. The Linux host checks
    /// its per-user state and prints a plain-text table, so it takes no options at all.
    /// </summary>
    public static string BuildCheckProductsArguments(
        RecompHostPlatform platform,
        string installFolderPath,
        string? retroRewindFolderPath = null
    )
    {
        if (string.IsNullOrWhiteSpace(installFolderPath))
            throw new ArgumentException("An install directory is required.", nameof(installFolderPath));

        if (platform != RecompHostPlatform.Windows)
            return "check-products";

        var arguments = new List<string> { "--check-products", "--install-dir", Quote(installFolderPath) };
        if (!string.IsNullOrWhiteSpace(retroRewindFolderPath))
        {
            arguments.Add("--retro-dir");
            arguments.Add(Quote(retroRewindFolderPath));
        }

        arguments.Add("--progress-json");
        return string.Join(' ', arguments);
    }

    /// <summary>
    /// Builds the arguments that make the setup executable print its own semantic version.
    /// </summary>
    public static string BuildVersionArguments() => "--version";

    /// <summary>
    /// Builds the arguments that make the Linux host remove its products, desktop entries and per-user state.
    /// </summary>
    public static string BuildUninstallArguments() => "uninstall";

    private static string Verb(RecompHostPlatform platform, string verb) => platform == RecompHostPlatform.Windows ? "--" + verb : verb;

    // The contract requires exactly one payload option (--download-retro-wfc-payload or
    // --skip-retro-wfc-payload) whenever --retro-dir is passed; WheelWizard always downloads.
    private const string RetroWfcPayloadArgument = "--download-retro-wfc-payload";

    // .NET splits ProcessStartInfo.Arguments with the same quoting rules on every platform.
    private static string Quote(string path)
    {
        var value = path.Trim();

        // A trailing backslash would escape the closing quote for CommandLineToArgvW, so drop redundant
        // separators and double the one that has to stay (a drive root such as "D:\").
        while (value.Length > 1 && value[^1] == '\\' && !value.EndsWith(@":\", StringComparison.Ordinal))
            value = value[..^1];
        if (value.EndsWith('\\'))
            value += '\\';

        return $"\"{value}\"";
    }
}
