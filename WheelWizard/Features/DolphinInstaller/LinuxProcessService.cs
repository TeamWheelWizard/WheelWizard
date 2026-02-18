using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WheelWizard.DolphinInstaller;

public interface ILinuxProcessService
{
    OperationResult<int> Run(string fileName, string arguments);
    Task<OperationResult<int>> RunWithProgressAsync(string fileName, string arguments, IProgress<int>? progress = null);
    Task<OperationResult> LaunchAndStopAsync(string fileName, string arguments, TimeSpan duration);
}

public sealed class LinuxProcessService : ILinuxProcessService
{
    public OperationResult<int> Run(string fileName, string arguments)
    {
        return TryCatch(
            () =>
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return -1;

                process.WaitForExit();
                return process.ExitCode;
            },
            $"Failed to run process: {fileName} {arguments}"
        );
    }

    public async Task<OperationResult<int>> RunWithProgressAsync(string fileName, string arguments, IProgress<int>? progress = null)
    {
        return await TryCatch(
            async () =>
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return -1;

                process.OutputDataReceived += (_, eventArgs) => ReportProgress(eventArgs.Data, progress);
                process.ErrorDataReceived += (_, eventArgs) => ReportProgress(eventArgs.Data, progress);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                return process.ExitCode;
            },
            $"Failed to run process: {fileName} {arguments}"
        );
    }

    public async Task<OperationResult> LaunchAndStopAsync(string fileName, string arguments, TimeSpan duration)
    {
        return await TryCatch(
            async () =>
            {
                using var process = new Process
                {
                    StartInfo = new()
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                process.Start();
                await Task.Delay(duration);

                if (!process.HasExited)
                    process.Kill();
            },
            $"Failed to run process: {fileName} {arguments}"
        );
    }

    private static void ReportProgress(string? output, IProgress<int>? progress)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        var match = Regex.Match(output, @"(\d+)%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int percent))
            progress?.Report(percent);
    }
}
