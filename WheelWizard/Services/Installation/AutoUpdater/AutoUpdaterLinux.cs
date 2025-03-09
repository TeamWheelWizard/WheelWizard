using System.Diagnostics;
using System.Runtime.InteropServices;
using WheelWizard.Helpers;
using WheelWizard.Models.Github;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Installation.AutoUpdater;

public class AutoUpdaterLinux : IUpdaterPlatform
{
    public GithubAsset? GetAssetForCurrentPlatform(GithubRelease release)
    {
        string identifier;
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm ||
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            identifier = "WheelWizard_arm64_Linux";
        }
        else
        {
            identifier = "WheelWizard_Linux";
        }
        
        return release.Assets.FirstOrDefault(asset =>
            asset.BrowserDownloadUrl.Contains(identifier, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ExecuteUpdateAsync(string downloadUrl)
    {
        var currentExecutablePath = Process.GetCurrentProcess().MainModule!.FileName;
        var currentExecutableName = Path.GetFileName(currentExecutablePath);
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

        // Download the new executable to a temporary file.
        var newFilePath = Path.Combine(currentFolder, currentExecutableName + "_new");
        if (File.Exists(newFilePath))
            File.Delete(newFilePath);

        await DownloadHelper.DownloadToLocationAsync(
            downloadUrl,
            newFilePath,
            Phrases.PopupText_UpdateWhWz,
            Phrases.PopupText_LatestWhWzGithub,
            ForceGivenFilePath: true);

        // Wait briefly to ensure the file is fully written.
        await Task.Delay(201);

        // Create and run the shell script to perform the update.
        CreateAndRunShellScript(currentExecutablePath, newFilePath);

        Environment.Exit(0);
    }

    private static void CreateAndRunShellScript(string currentFilePath, string newFilePath)
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

        var scriptFilePath = Path.Combine(currentFolder, "update.sh");
        var originalFileName = Path.GetFileName(currentFilePath);
        var newFileName = Path.GetFileName(newFilePath);

        var scriptContent = $@"#!/bin/bash
echo 'Starting update process...'

# Give a short delay to ensure the application has exited
sleep 1

echo 'Replacing old executable...'
rm -f ""{Path.Combine(currentFolder, originalFileName)}""
mv ""{Path.Combine(currentFolder, newFileName)}"" ""{Path.Combine(currentFolder, originalFileName)}""
chmod +x ""{Path.Combine(currentFolder, originalFileName)}""

echo 'Starting the updated application...'
nohup ""{Path.Combine(currentFolder, originalFileName)}"" > /dev/null 2>&1 &

echo 'Cleaning up...'
rm -- ""{scriptFilePath}""

echo 'Update completed successfully.'
";
        File.WriteAllText(scriptFilePath, scriptContent);

        // Ensure the script is executable.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{scriptFilePath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit();
        }
        catch (Exception ex)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Unable to update Wheel Wizard")
                .SetInfoText($"Failed to set execute permission for the update script. {ex.Message}")
                .ShowDialog();
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = $"\"{scriptFilePath}\"",
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
