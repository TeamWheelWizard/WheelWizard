using System.Diagnostics;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Processes;

namespace WheelWizard.AutoUpdating.Platforms;

public class LinuxUpdatePlatform(
    IFileSystem fileSystem,
    IDownloadService downloads,
    IApplicationProcess application,
    IProcessLauncher processes
) : IUpdatePlatform
{
    public bool SupportsAutomaticUpdate => true;

    public GithubAsset? GetAssetForCurrentPlatform(GithubRelease release)
    {
        string identifier;
        if (application.Architecture == Architecture.Arm || application.Architecture == Architecture.Arm64)
        {
            identifier = "WheelWizard_arm64_Linux";
        }
        else
        {
            identifier = "WheelWizard_Linux";
        }

        return release.Assets.FirstOrDefault(asset => asset.BrowserDownloadUrl.Contains(identifier, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OperationResult> ExecuteUpdateAsync(
        string downloadUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentExecutablePath = application.ExecutablePath;
            if (currentExecutablePath is null)
                return Fail(t("message_warning.unable_update_wh_wz.extra.reason_location"));

            var currentExecutableName = fileSystem.Path.GetFileName(currentExecutablePath);
            var currentFolder = fileSystem.Path.GetDirectoryName(currentExecutablePath);

            if (currentFolder is null)
                return Fail(t("message_warning.unable_update_wh_wz.extra.reason_location"));

            // Download the new executable to a temporary file.
            var newFilePath = fileSystem.Path.Combine(currentFolder, currentExecutableName + "_new");
            if (fileSystem.File.Exists(newFilePath))
                fileSystem.File.Delete(newFilePath);

            progress?.Report(new DownloadProgress(0, null));
            var downloadResult = await downloads.DownloadAsync(downloadUrl, newFilePath, true, progress, cancellationToken);
            if (downloadResult.IsFailure)
                return downloadResult.Error;
            cancellationToken.ThrowIfCancellationRequested();
            if (!fileSystem.File.Exists(newFilePath))
                return Fail("The downloaded update executable could not be found.");

            // Create and run the shell script to perform the update.
            var scriptResult = CreateAndRunShellScript(currentExecutablePath, newFilePath);
            if (scriptResult.IsFailure)
                return scriptResult;

            application.Exit(0);

            return Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to prepare application update: {ex.Message}", Exception = ex };
        }
    }

    private OperationResult CreateAndRunShellScript(string currentFilePath, string newFilePath)
    {
        var currentFolder = fileSystem.Path.GetDirectoryName(currentFilePath);
        if (currentFolder is null)
            return Fail(t("message_warning.unable_update_wh_wz.extra.reason_location"));

        var scriptFilePath = fileSystem.Path.Combine(currentFolder, "update.sh");
        var originalFileName = fileSystem.Path.GetFileName(currentFilePath);
        var newFileName = fileSystem.Path.GetFileName(newFilePath);

        var scriptContent = $"""
            #!/usr/bin/env sh
            echo 'Starting update process...'

            # Give a short delay to ensure the application has exited
            sleep 1

            echo 'Replacing old executable...'
            rm -f {ShellQuoting.QuoteUnixArgument(fileSystem.Path.Combine(currentFolder, originalFileName))}
            mv {ShellQuoting.QuoteUnixArgument(fileSystem.Path.Combine(currentFolder, newFileName))} {ShellQuoting.QuoteUnixArgument(
                fileSystem.Path.Combine(currentFolder, originalFileName)
            )}
            chmod +x {ShellQuoting.QuoteUnixArgument(fileSystem.Path.Combine(currentFolder, originalFileName))}

            echo 'Starting the updated application...'
            nohup {ShellQuoting.QuoteUnixArgument(fileSystem.Path.Combine(currentFolder, originalFileName))} > /dev/null 2>&1 &

            echo 'Cleaning up...'
            rm -- {ShellQuoting.QuoteUnixArgument(scriptFilePath)}

            echo 'Update completed successfully.'
            
            """;
        fileSystem.File.WriteAllText(scriptFilePath, scriptContent);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/env",
            ArgumentList = { "sh", "--", scriptFilePath },
            CreateNoWindow = false,
            UseShellExecute = false,
            WorkingDirectory = currentFolder,
        };

        return TryCatch(() => processes.Start(processStartInfo), errorMessage: "Failed to execute the update script.");
    }
}
