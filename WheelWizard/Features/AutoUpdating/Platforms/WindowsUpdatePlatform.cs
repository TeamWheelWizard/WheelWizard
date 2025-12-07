using System.Diagnostics;
using System.IO.Abstractions;
using System.Security.Principal;
using WheelWizard.GitHub.Domain;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.AutoUpdating.Platforms;

public class WindowsUpdatePlatform(IFileSystem fileSystem) : IUpdatePlatform
{
    public GithubAsset? GetAssetForCurrentPlatform(GithubRelease release)
    {
        // Select the first asset ending with ".exe"
        return release.Assets.FirstOrDefault(asset => asset.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OperationResult> ExecuteUpdateAsync(string downloadUrl)
    {
        // If running as administrator, update immediately.
        if (IsAdministrator())
            return await UpdateAsync(downloadUrl);

        // Otherwise, ask if the user wants to restart as admin.
        var restartAsAdmin = await new YesNoWindow()
            .SetMainText(Phrases.Question_UpdateAdmin_Title)
            .SetExtraText(Phrases.Question_UpdateAdmin_Extra)
            .AwaitAnswer();

        if (!restartAsAdmin)
            return await UpdateAsync(downloadUrl);

        return RestartAsAdmin();
    }

    private static OperationResult RestartAsAdmin()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Environment.ProcessPath,
            Verb = "runas", // This verb asks for elevation.
        };

        return TryCatch(
            () =>
            {
                Process.Start(startInfo);
                Environment.Exit(0);
            },
            errorMessage: Phrases.MessageError_RestartAdminFail_Extra
        );
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private async Task<OperationResult> UpdateAsync(string downloadUrl)
    {
        var currentExecutablePath = Environment.ProcessPath;
        if (currentExecutablePath is null)
            return Fail(Phrases.MessageWarning_UnableUpdateWhWz_Extra_ReasonLocation);

        var currentExecutableName = fileSystem.Path.GetFileNameWithoutExtension(currentExecutablePath);
        var currentFolder = fileSystem.Path.GetDirectoryName(currentExecutablePath);

        if (currentFolder is null)
            return Fail(Phrases.MessageWarning_UnableUpdateWhWz_Extra_ReasonLocation);

        // Download new executable to a temporary file.
        var newFilePath = fileSystem.Path.Combine(currentFolder, currentExecutableName + "_new.exe");
        if (fileSystem.File.Exists(newFilePath))
            fileSystem.File.Delete(newFilePath);

        await DownloadHelper.DownloadToLocationAsync(
            downloadUrl,
            newFilePath,
            Phrases.Progress_UpdateWhWz,
            Phrases.Progress_LatestWhWzGithub,
            ForceGivenFilePath: true
        );

        // Wait briefly to ensure the file is saved on disk.
        await Task.Delay(200);

        // Create and run the PowerShell script to perform the update.
        var scriptResult = CreateAndRunPowerShellScript(currentExecutablePath, newFilePath);
        if (scriptResult.IsFailure)
            return scriptResult;

        Environment.Exit(0);

        return Ok();
    }

    private OperationResult CreateAndRunPowerShellScript(string currentFilePath, string newFilePath)
    {
        var currentFolder = fileSystem.Path.GetDirectoryName(currentFilePath);
        if (currentFolder is null)
            return Fail(Phrases.MessageWarning_UnableUpdateWhWz_Extra_ReasonLocation);

        var scriptFilePath = fileSystem.Path.Combine(currentFolder, "update.ps1");
        var originalFileName = fileSystem.Path.GetFileName(currentFilePath);
        var newFileName = fileSystem.Path.GetFileName(newFilePath);

        var scriptContent = $$"""

            Write-Output 'Starting update process...'

            # Wait for the original application to exit
            while (Get-Process -Name {{EnvHelper.SingleQuotePath(fileSystem.Path.GetFileNameWithoutExtension(originalFileName))}} -ErrorAction SilentlyContinue) {
                Write-Output 'Waiting for {{originalFileName}} to exit...'
                Start-Sleep -Seconds 1
            }

            Write-Output 'Deleting old executable...'
            $maxRetries = 5
            $retryCount = 0
            $deleted = $false

            while (-not $deleted -and $retryCount -lt $maxRetries) {
                try {
                    Remove-Item -Path {{EnvHelper.SingleQuotePath(fileSystem.Path.Combine(currentFolder, originalFileName))}} -Force -ErrorAction Stop
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
                Rename-Item -Path {{EnvHelper.SingleQuotePath(fileSystem.Path.Combine(
                currentFolder,
                newFileName
            ))}} -NewName {{EnvHelper.SingleQuotePath(originalFileName)}} -ErrorAction Stop
            }
            catch {
                Write-Output 'Failed to rename {{newFileName}} to {{originalFileName}}. Update aborted.'
                pause
                exit 1
            }

            Write-Output 'Starting the updated application...'
            Start-Process -FilePath {{EnvHelper.SingleQuotePath(fileSystem.Path.Combine(currentFolder, originalFileName))}}

            Write-Output 'Cleaning up...'
            Remove-Item -Path {{EnvHelper.SingleQuotePath(scriptFilePath)}} -Force

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

        return TryCatch(() => Process.Start(processStartInfo), errorMessage: "Failed to execute the update script.");
    }
}
