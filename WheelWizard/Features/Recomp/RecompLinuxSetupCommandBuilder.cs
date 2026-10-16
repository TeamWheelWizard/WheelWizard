using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// Builds the command lines of the recomp's Linux AppImage. Unlike the Windows setup, the AppImage takes
/// a subcommand (<c>install</c>, <c>launch-retro</c>, ...) and owns its install locations, so Wheel Wizard
/// never passes <c>--install-dir</c> or <c>--portable</c>: the products, <c>install-state.json</c> and
/// <c>Config.toml</c> all live where the AppImage puts them, under the user's XDG data directory.
/// Every method returns the argument vector as separate values, handed to
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> without any quoting.
/// </summary>
public static class RecompLinuxSetupCommandBuilder
{
    /// <summary>
    /// Builds the install command. With <paramref name="gameFolderPath"/> the AppImage validates and reads
    /// Wheel Wizard's extracted game in place; without it, it reuses the game it already knows and only
    /// retranslates and recompiles what changed, which is how a repair or a Retro Rewind update is run.
    /// </summary>
    public static IReadOnlyList<string> BuildInstallArguments(
        string? gameFolderPath,
        string? retroRewindFolderPath,
        RecompRetroWfcPayloadMode retroWfcPayloadMode = RecompRetroWfcPayloadMode.Download
    )
    {
        var arguments = new List<string> { "install" };
        if (!string.IsNullOrWhiteSpace(gameFolderPath))
        {
            arguments.Add("--game-dir");
            arguments.Add(gameFolderPath);
        }

        // The AppImage requires exactly one payload option whenever a Retro Rewind source is passed,
        // and rejects either option without one.
        if (!string.IsNullOrWhiteSpace(retroRewindFolderPath))
        {
            arguments.Add("--retro-dir");
            arguments.Add(retroRewindFolderPath);
            arguments.Add(
                retroWfcPayloadMode == RecompRetroWfcPayloadMode.Skip ? "--skip-retro-wfc-payload" : "--download-retro-wfc-payload"
            );
        }

        arguments.Add("--progress-json");
        return arguments;
    }

    /// <summary>Starts an installed product. Wheel Wizard is the Retro Rewind frontend, so it launches that one.</summary>
    public static IReadOnlyList<string> BuildLaunchArguments(bool retroRewind) => [retroRewind ? "launch-retro" : "launch-base"];

    /// <summary>Removes every product the AppImage installed, plus their application-menu entries.</summary>
    public static IReadOnlyList<string> BuildUninstallArguments() => ["uninstall"];

    /// <summary>Makes the AppImage print its own semantic version.</summary>
    public static IReadOnlyList<string> BuildVersionArguments() => ["--version"];

    /// <summary>Points the installation at Wheel Wizard's extracted game without rebuilding anything.</summary>
    public static IReadOnlyList<string> BuildLinkGameDirArguments(string gameFolderPath)
    {
        if (string.IsNullOrWhiteSpace(gameFolderPath))
            throw new ArgumentException("The extracted game folder is required.", nameof(gameFolderPath));
        return ["link-game-dir", "--game-dir", gameFolderPath, "--progress-json"];
    }
}
