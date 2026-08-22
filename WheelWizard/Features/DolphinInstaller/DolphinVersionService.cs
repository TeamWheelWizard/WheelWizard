using System.Runtime.InteropServices;
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

        // Invoke the configured Dolphin the way LaunchDolphin does, just with --version.
        string stdOut;
        string stdErr;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var windowsResult = processService.Run(dolphinLocation, "--version", out stdOut, out stdErr);
            return ReadOutput(windowsResult, stdOut, stdErr);
        }

        // a broken Qt platform configuration should not be what stops us from reading the version.
        // + flatpak dolphin runs in its own environment, so passing it here should not reach it anyway.
        List<string> arguments = [];
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !PathManager.IsFlatpakDolphinFilePath(dolphinLocation))
            arguments.Add("QT_QPA_PLATFORM=xcb");

        arguments.AddRange(["sh", "-c", "--", $"{dolphinLocation} --version"]);

        var result = processService.Run("/usr/bin/env", arguments, out stdOut, out stdErr);
        return ReadOutput(result, stdOut, stdErr);
    }

    private static string? ReadOutput(OperationResult<int> result, string stdOut, string stdErr)
    {
        if (result.IsFailure || result.Value != 0)
            return null;

        // Some builds print the version to stderr, so take whichever stream actually has something.
        return string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut;
    }
}
