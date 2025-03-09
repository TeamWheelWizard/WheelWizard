using System.Diagnostics;
using System.Security.Principal;
using WheelWizard.Helpers;
using WheelWizard.Models.Github;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Installation.AutoUpdater;

public class AutoUpdaterWindows : IUpdaterPlatform
{
    public GithubAsset? GetAssetForCurrentPlatform(GithubRelease release)
    {
        // Select the first asset ending with ".exe"
        return release.Assets.FirstOrDefault(asset =>
            asset.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    public async Task ExecuteUpdateAsync(string downloadUrl)
    {
        // If running as administrator, update immediately.
        if (IsAdministrator())
        {
            await UpdateAsync(downloadUrl);
            return;
        }

        // Otherwise, ask if the user wants to restart as admin.
        var adminQuestion = new YesNoWindow()
            .SetMainText(Phrases.PopupText_UpdateAdmin)
            .SetExtraText(Phrases.PopupText_UpdateAdminExplained);

        if (await adminQuestion.AwaitAnswer())
        {
            RestartAsAdmin();
        }
        else
        {
            await UpdateAsync(downloadUrl);
        }
    }

    private static void RestartAsAdmin()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = GetActualExecutablePath(),
            Verb = "runas" // This verb asks for elevation.
        };

        try
        {
            Process.Start(startInfo);
            Environment.Exit(0);
        }
        catch (Exception)
        {
            new YesNoWindow().SetMainText(Phrases.PopupText_RestartAdminFail);
        }
    }

    private static string GetActualExecutablePath()
    {
        using (var process = Process.GetCurrentProcess())
        {
            return process.MainModule.FileName;
        }
    }

    private static bool IsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private static async Task UpdateAsync(string downloadUrl)
    {
        var currentExecutablePath = Process.GetCurrentProcess().MainModule!.FileName;
        var currentExecutableName = Path.GetFileNameWithoutExtension(currentExecutablePath);
        var currentFolder = Path.GetDirectoryName(currentExecutablePath);

        if (currentFolder is null)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Unable to update Wheel Wizard")
                .SetInfoText(Phrases.PopupText_UnableUpdateWhWz_ReasonLocation)
                .ShowDialog();
            return;
        }

        // Download new executable to a temporary file.
        var newFilePath = Path.Combine(currentFolder, currentExecutableName + "_new.exe");
        if (File.Exists(newFilePath))
            File.Delete(newFilePath);

        await DownloadHelper.DownloadToLocationAsync(
            downloadUrl,
            newFilePath,
            Phrases.PopupText_UpdateWhWz,
            Phrases.PopupText_LatestWhWzGithub,
            ForceGivenFilePath: true);

        // Wait briefly to ensure the file is saved on disk.
        await Task.Delay(200);

        // Create and run the PowerShell script to perform the update.
        CreateAndRunPowerShellScript(currentExecutablePath, newFilePath);

        Environment.Exit(0);
    }

    private static void CreateAndRunPowerShellScript(string currentFilePath, string newFilePath)
    {
        var currentFolder = Path.GetDirectoryName(currentFilePath);
        if (currentFolder is null)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Unable to update Wheel Wizard")
                .SetInfoText(Phrases.PopupText_UnableUpdateWhWz_ReasonLocation)
                .ShowDialog();
            return;
        }

        var scriptFilePath = Path.Combine(currentFolder, "update.ps1");
        var originalFileName = Path.GetFileName(currentFilePath);
        var newFileName = Path.GetFileName(newFilePath);

        var scriptContent = $@"
Write-Output 'Starting update process...'

# Wait for the original application to exit
while (Get-Process -Name '{Path.GetFileNameWithoutExtension(originalFileName)}' -ErrorAction SilentlyContinue) {{
    Write-Output 'Waiting for {originalFileName} to exit...'
    Start-Sleep -Seconds 1
}}

Write-Output 'Deleting old executable...'
$maxRetries = 5
$retryCount = 0
$deleted = $false

while (-not $deleted -and $retryCount -lt $maxRetries) {{
    try {{
        Remove-Item -Path '{Path.Combine(currentFolder, originalFileName)}' -Force -ErrorAction Stop
        $deleted = $true
    }}
    catch {{
        Write-Output 'Failed to delete {originalFileName}. Retrying in 2 seconds...'
        Start-Sleep -Seconds 2
        $retryCount++
    }}
}}

if (-not $deleted) {{
    Write-Output 'Could not delete {originalFileName}. Update aborted.'
    pause
    exit 1
}}

Write-Output 'Renaming new executable...'
try {{
    Rename-Item -Path '{Path.Combine(currentFolder, newFileName)}' -NewName '{originalFileName}' -ErrorAction Stop
}}
catch {{
    Write-Output 'Failed to rename {newFileName} to {originalFileName}. Update aborted.'
    pause
    exit 1
}}

Write-Output 'Starting the updated application...'
Start-Process -FilePath '{Path.Combine(currentFolder, originalFileName)}'

Write-Output 'Cleaning up...'
Remove-Item -Path '{scriptFilePath}' -Force

Write-Output 'Update completed successfully.'
";
        File.WriteAllText(scriptFilePath, scriptContent);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFilePath}\"",
            CreateNoWindow = false,
            UseShellExecute = false,
            WorkingDirectory = currentFolder
        };

        try
        {
            Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Unable to update Wheel Wizard")
                .SetInfoText($"Failed to execute the update script. {ex.Message}")
                .ShowDialog();
        }
    }
}
