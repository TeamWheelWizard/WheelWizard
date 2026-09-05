using System.Text;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// Builds the command lines of the recomp's Linux AppImage. Unlike the Windows setup, the AppImage takes
/// a subcommand (<c>install</c>, <c>launch-retro</c>, ...) and owns its install locations, so Wheel Wizard
/// never passes <c>--install-dir</c> or <c>--portable</c>: the products, <c>install-state.json</c> and
/// <c>Config.toml</c> all live where the AppImage puts them, under the user's XDG data directory.
/// </summary>
public static class RecompLinuxSetupCommandBuilder
{
    /// <summary>
    /// Builds the install command. With <paramref name="gameFilePath"/> the AppImage validates and
    /// extracts the disc first; without it, it reuses the assets it already extracted and only
    /// retranslates and recompiles what changed, which is how a repair or a Retro Rewind update is run.
    /// </summary>
    public static string BuildInstallArguments(
        string? gameFilePath,
        string? retroRewindFolderPath,
        RecompRetroWfcPayloadMode retroWfcPayloadMode = RecompRetroWfcPayloadMode.Download
    )
    {
        var arguments = new List<string> { "install" };
        if (!string.IsNullOrWhiteSpace(gameFilePath))
        {
            arguments.Add("--game");
            arguments.Add(Quote(gameFilePath));
        }

        // The AppImage requires exactly one payload option whenever a Retro Rewind source is passed,
        // and rejects either option without one.
        if (!string.IsNullOrWhiteSpace(retroRewindFolderPath))
        {
            arguments.Add("--retro-dir");
            arguments.Add(Quote(retroRewindFolderPath));
            arguments.Add(
                retroWfcPayloadMode == RecompRetroWfcPayloadMode.Skip ? "--skip-retro-wfc-payload" : "--download-retro-wfc-payload"
            );
        }

        arguments.Add("--progress-json");
        return string.Join(' ', arguments);
    }

    /// <summary>Starts an installed product. Wheel Wizard is the Retro Rewind frontend, so it launches that one.</summary>
    public static string BuildLaunchArguments(bool retroRewind) => retroRewind ? "launch-retro" : "launch-base";

    /// <summary>Removes every product the AppImage installed, plus their application-menu entries.</summary>
    public static string BuildUninstallArguments() => "uninstall";

    /// <summary>Makes the AppImage print its own semantic version.</summary>
    public static string BuildVersionArguments() => "--version";

    /// <summary>
    /// Quotes a path for <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/>, which .NET splits
    /// with the same rules on every platform: a backslash is literal unless it precedes a quote, and a
    /// quote inside the value is escaped with a backslash. No shell ever sees this string.
    /// </summary>
    public static string Quote(string path)
    {
        var builder = new StringBuilder("\"");
        var pendingBackslashes = 0;
        foreach (var character in path.Trim())
        {
            if (character == '\\')
            {
                pendingBackslashes++;
                continue;
            }

            // Backslashes before a quote are halved by the parser, so double them, then escape the quote.
            builder.Append('\\', character == '"' ? pendingBackslashes * 2 + 1 : pendingBackslashes);
            builder.Append(character);
            pendingBackslashes = 0;
        }

        // Trailing backslashes precede the closing quote and would escape it: double them.
        builder.Append('\\', pendingBackslashes * 2);
        return builder.Append('"').ToString();
    }
}
