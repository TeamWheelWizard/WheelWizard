using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WheelWizard.Services;

namespace WheelWizard.DolphinInstaller;

public interface IDolphinVersionService
{
    /// <summary>
    /// Reads the version of the Dolphin the user has configured. Never throws: anything we cannot
    /// read or recognize comes back as <see cref="DolphinVersionStatus.Unknown"/>.
    /// </summary>
    (DolphinVersionStatus Status, string? Version) CheckConfiguredDolphin();
}

public sealed class DolphinVersionService(ILinuxProcessService processService) : IDolphinVersionService
{
    private static readonly Regex FlatpakVersionLine = new(@"^\s*Version:\s*(?<version>.+)$", RegexOptions.Multiline);

    /// <summary>
    /// The HTTP user agent Dolphin bakes into its binary, e.g. "Dolphin/2606". Deliberately not the
    /// looser "Dolphin 2606" window-title form: the binary also embeds changelog text mentioning
    /// other versions ("Dolphin 5.0-12188"), and mistaking one of those for the installed version
    /// would keep warning people who already updated.
    /// </summary>
    private static readonly Regex WindowsBinaryVersionPattern = new(@"Dolphin/[0-9][\w.\-]*", RegexOptions.Compiled);

    public (DolphinVersionStatus Status, string? Version) CheckConfiguredDolphin()
    {
        string? versionText;
        try
        {
            versionText = ReadVersionText(PathManager.DolphinFilePath);
        }
        catch
        {
            // Probing must never be the reason someone cannot launch.
            versionText = null;
        }

        var status = DolphinVersion.GetStatus(versionText, out var foundVersion);
        return (status, foundVersion);
    }

    private string? ReadVersionText(string dolphinLocation)
    {
        if (string.IsNullOrWhiteSpace(dolphinLocation))
            return null;

        // Flatpak: ask Flatpak instead of running Dolphin, which is faster and cannot pop a window.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && PathManager.IsFlatpakDolphinFilePath(dolphinLocation))
        {
            var appId = PathManager.ExtractDolphinFlatpakAppId(dolphinLocation);
            var flatpakResult = processService.Run("flatpak", $"info {appId}", out var flatpakStdOut, out _);
            if (flatpakResult.IsFailure || flatpakResult.Value != 0)
                return null;

            return FlatpakVersionLine.Match(flatpakStdOut) is { Success: true } match ? match.Groups["version"].Value : null;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // On Unix the configured value can be a bare command rather than a path, so let a shell
        // resolve it the same way LaunchDolphin does. On Windows it is always a real path.
        var (fileName, arguments) = isWindows
            ? (dolphinLocation, "--version")
            : ("/usr/bin/env", $"sh -c -- \"{dolphinLocation} --version\"");

        var result = processService.Run(fileName, arguments, out var stdOut, out var stdErr);
        if (result.IsSuccess && result.Value == 0)
        {
            // Some builds print the version to stderr, so take whichever stream actually has something.
            var output = string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut;
            if (DolphinVersion.GetStatus(output) != DolphinVersionStatus.Unknown)
                return output;
        }

        // Dolphin on Windows is a GUI-subsystem exe: when Wheel Wizard itself was started from a
        // console (a dev running it from a terminal), Dolphin attaches to that console and writes
        // its version there instead of into our pipe. Fall back to the version baked into the binary.
        return isWindows ? ReadWindowsBinaryVersion(dolphinLocation) : null;
    }

    private static string? ReadWindowsBinaryVersion(string dolphinLocation)
    {
        if (!File.Exists(dolphinLocation))
            return null;

        var binaryText = Encoding.ASCII.GetString(File.ReadAllBytes(Path.GetFullPath(dolphinLocation)));
        var match = WindowsBinaryVersionPattern.Match(binaryText);
        return match.Success ? match.Value : null;
    }
}
