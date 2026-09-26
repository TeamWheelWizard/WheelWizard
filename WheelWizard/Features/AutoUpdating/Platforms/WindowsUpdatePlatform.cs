using System.Diagnostics;
using System.IO.Abstractions;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Processes;

namespace WheelWizard.AutoUpdating.Platforms;

public class WindowsUpdatePlatform(
    IFileSystem fileSystem,
    IDownloadService downloads,
    IApplicationProcess application,
    IProcessLauncher processes,
    IUpdatePresentation presentation
) : IUpdatePlatform
{
    public bool SupportsAutomaticUpdate => true;

    public GithubAsset? GetAssetForCurrentPlatform(GithubRelease release)
    {
        // Select the first asset ending with ".exe"
        return release.Assets.FirstOrDefault(asset => asset.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
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
            // If running as administrator, update immediately.
            if (application.IsAdministrator)
                return await UpdateAsync(downloadUrl, progress, cancellationToken);

            // Otherwise, ask if the user wants to restart as admin.
            var restartAsAdmin = await presentation.ConfirmElevationAsync();

            if (!restartAsAdmin)
                return await UpdateAsync(downloadUrl, progress, cancellationToken);

            return RestartAsAdmin();
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

    private OperationResult RestartAsAdmin()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = application.WorkingDirectory,
            FileName = application.ExecutablePath,
            Verb = "runas", // This verb asks for elevation.
        };

        return TryCatch(
            () =>
            {
                processes.Start(startInfo);
                application.Exit(0);
            },
            errorMessage: t("message_error.restart_admin_fail.extra")
        );
    }

    private async Task<OperationResult> UpdateAsync(
        string downloadUrl,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var currentExecutablePath = application.ExecutablePath;
        if (currentExecutablePath is null)
            return Fail(t("message_warning.unable_update_wh_wz.extra.reason_location"));

        var currentExecutableName = fileSystem.Path.GetFileNameWithoutExtension(currentExecutablePath);
        var currentFolder = fileSystem.Path.GetDirectoryName(currentExecutablePath);

        if (currentFolder is null)
            return Fail(t("message_warning.unable_update_wh_wz.extra.reason_location"));

        // Download new executable to a temporary file.
        var newFilePath = fileSystem.Path.Combine(currentFolder, currentExecutableName + "_new.exe");
        if (fileSystem.File.Exists(newFilePath))
            fileSystem.File.Delete(newFilePath);

        progress?.Report(new DownloadProgress(0, null));
        var downloadResult = await downloads.DownloadAsync(downloadUrl, newFilePath, true, progress, cancellationToken);
        if (downloadResult.IsFailure)
            return downloadResult.Error;
        cancellationToken.ThrowIfCancellationRequested();
        if (!fileSystem.File.Exists(newFilePath))
            return Fail("The downloaded update executable could not be found.");

        // Create and run the PowerShell script to perform the update.
        var scriptResult = CreateAndRunPowerShellScript(currentExecutablePath, newFilePath);
        if (scriptResult.IsFailure)
            return scriptResult;

        application.Exit(0);

        return Ok();
    }

    private OperationResult CreateAndRunPowerShellScript(string currentFilePath, string newFilePath)
    {
        var currentFolder = fileSystem.Path.GetDirectoryName(currentFilePath);
        if (currentFolder is null)
            return Fail(t("message_warning.unable_update_wh_wz.extra.reason_location"));

        var scriptFilePath = fileSystem.Path.Combine(currentFolder, "update.ps1");
        var originalFileName = fileSystem.Path.GetFileName(currentFilePath);
        var newFileName = fileSystem.Path.GetFileName(newFilePath);

        var scriptContent = $$"""

            Write-Output 'Starting update process...'

            # Wait for the original application to exit
            while (Get-Process -Name {{ShellQuoting.QuotePowerShellArgument(fileSystem.Path.GetFileNameWithoutExtension(originalFileName))}} -ErrorAction SilentlyContinue) {
                Write-Output 'Waiting for {{originalFileName}} to exit...'
                Start-Sleep -Seconds 1
            }

            Write-Output 'Deleting old executable...'
            $maxRetries = 5
            $retryCount = 0
            $deleted = $false

            while (-not $deleted -and $retryCount -lt $maxRetries) {
                try {
                    Remove-Item -Path {{ShellQuoting.QuotePowerShellArgument(fileSystem.Path.Combine(currentFolder, originalFileName))}} -Force -ErrorAction Stop
                    $deleted = $true
                }
                catch {
                    Write-Output 'Failed to delete {{originalFileName}}. Retrying in 2 seconds...'
                    Start-Sleep -Seconds 2
                    $retryCount++
                }
            }

            if (-not $deleted) {
                Write-Output 'Could not delete {{originalFileName}}. Update aborted.'
                pause
                exit 1
            }

            Write-Output 'Renaming new executable...'
            try {
                Rename-Item -Path {{ShellQuoting.QuotePowerShellArgument(fileSystem.Path.Combine(
                currentFolder,
                newFileName
            ))}} -NewName {{ShellQuoting.QuotePowerShellArgument(originalFileName)}} -ErrorAction Stop
            }
            catch {
                Write-Output 'Failed to rename {{newFileName}} to {{originalFileName}}. Update aborted.'
                pause
                exit 1
            }

            Write-Output 'Starting the updated application...'
            Start-Process -FilePath {{ShellQuoting.QuotePowerShellArgument(fileSystem.Path.Combine(currentFolder, originalFileName))}}

            Write-Output 'Cleaning up...'
            Remove-Item -Path {{ShellQuoting.QuotePowerShellArgument(scriptFilePath)}} -Force

            Write-Output 'Update completed successfully.'

            """;

        fileSystem.File.WriteAllText(scriptFilePath, scriptContent);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptFilePath },
            CreateNoWindow = false,
            UseShellExecute = false,
            WorkingDirectory = currentFolder,
        };

        return TryCatch(() => processes.Start(processStartInfo), errorMessage: "Failed to execute the update script.");
    }
}
