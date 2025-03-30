using System.Diagnostics;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using WheelWizard.GitHub.Domain;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;

namespace WheelWizard.AutoUpdating.Platforms;

public class LinuxUpdatePlatform(IFileSystem fileSystem) : IUpdatePlatform
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

    public async Task<OperationResult> ExecuteUpdateAsync(string downloadUrl)
    {
        var currentExecutablePath = Environment.ProcessPath;
        if (currentExecutablePath is null)
            return Fail(Phrases.PopupText_UnableUpdateWhWz_ReasonLocation);

        var currentExecutableName = fileSystem.Path.GetFileName(currentExecutablePath);
        var currentFolder = fileSystem.Path.GetDirectoryName(currentExecutablePath);

        if (currentFolder is null)
            return Fail(Phrases.PopupText_UnableUpdateWhWz_ReasonLocation);

        // Download the new executable to a temporary file.
        var newFilePath = fileSystem.Path.Combine(currentFolder, currentExecutableName + "_new");
        if (fileSystem.File.Exists(newFilePath))
            fileSystem.File.Delete(newFilePath);

        await DownloadHelper.DownloadToLocationAsync(
            downloadUrl,
            newFilePath,
            Phrases.PopupText_UpdateWhWz,
            Phrases.PopupText_LatestWhWzGithub,
            forceGivenFilePath: true);

        // Wait briefly to ensure the file is fully written.
        await Task.Delay(201);

        // Create and run the shell script to perform the update.
        var scriptResult = CreateAndRunShellScript(currentExecutablePath, newFilePath);
        if (scriptResult.IsFailure)
            return scriptResult;

        Environment.Exit(0);

        return Ok();
    }

    private OperationResult CreateAndRunShellScript(string currentFilePath, string newFilePath)
    {
        var currentFolder = fileSystem.Path.GetDirectoryName(currentFilePath);
        if (currentFolder is null)
            return Fail(Phrases.PopupText_UnableUpdateWhWz_ReasonLocation);

        var scriptFilePath = fileSystem.Path.Combine(currentFolder, "update.sh");
        var originalFileName = fileSystem.Path.GetFileName(currentFilePath);
        var newFileName = fileSystem.Path.GetFileName(newFilePath);

        var scriptContent =
            $"""
             #!/usr/bin/env sh
             echo 'Starting update process...'

             # Give a short delay to ensure the application has exited
             sleep 1

             echo 'Replacing old executable...'
             rm -f "{fileSystem.Path.Combine(currentFolder, originalFileName)}"
             mv "{fileSystem.Path.Combine(currentFolder, newFileName)}" "{fileSystem.Path.Combine(currentFolder, originalFileName)}"
             chmod +x "{fileSystem.Path.Combine(currentFolder, originalFileName)}"

             echo 'Starting the updated application...'
             nohup "{fileSystem.Path.Combine(currentFolder, originalFileName)}" > /dev/null 2>&1 &

             echo 'Cleaning up...'
             rm -- "{scriptFilePath}"

             echo 'Update completed successfully.'

             """;
        fileSystem.File.WriteAllText(scriptFilePath, scriptContent);

        // Ensure the script is executable.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/env",
                ArgumentList =
                {
                    "chmod",
                    "+x",
                    "--",
                    scriptFilePath,
                },
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit();
        }
        catch (Exception ex)
        {
            return Fail(new()
            {
                Message = "Failed to set execute permission for the update script.",
                Exception = ex
            });
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/env",
            ArgumentList =
            {
                "sh",
                "--",
                scriptFilePath,
            },
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
            return Fail(new()
            {
                Message = "Failed to execute the update script.",
                Exception = ex
            });
        }

        return Ok();
    }
}
