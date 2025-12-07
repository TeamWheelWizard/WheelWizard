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
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm || RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            identifier = "WheelWizard_arm64_Linux";
        }
        else
        {
            identifier = "WheelWizard_Linux";
        }

        return release.Assets.FirstOrDefault(asset => asset.BrowserDownloadUrl.Contains(identifier, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OperationResult> ExecuteUpdateAsync(string downloadUrl)
    {
        var currentExecutablePath = Environment.ProcessPath;
        if (currentExecutablePath is null)
            return Fail(Phrases.MessageWarning_UnableUpdateWhWz_Extra_ReasonLocation);

        var currentExecutableName = fileSystem.Path.GetFileName(currentExecutablePath);
        var currentFolder = fileSystem.Path.GetDirectoryName(currentExecutablePath);

        if (currentFolder is null)
            return Fail(Phrases.MessageWarning_UnableUpdateWhWz_Extra_ReasonLocation);

        // Download the new executable to a temporary file.
        var newFilePath = fileSystem.Path.Combine(currentFolder, currentExecutableName + "_new");
        if (fileSystem.File.Exists(newFilePath))
            fileSystem.File.Delete(newFilePath);

        await DownloadHelper.DownloadToLocationAsync(
            downloadUrl,
            newFilePath,
            Phrases.Progress_UpdateWhWz,
            Phrases.Progress_LatestWhWzGithub,
            ForceGivenFilePath: true
        );

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
            return Fail(Phrases.MessageWarning_UnableUpdateWhWz_Extra_ReasonLocation);

        var scriptFilePath = fileSystem.Path.Combine(currentFolder, "update.sh");
        var originalFileName = fileSystem.Path.GetFileName(currentFilePath);
        var newFileName = fileSystem.Path.GetFileName(newFilePath);

        var scriptContent = $"""
            #!/usr/bin/env sh
            echo 'Starting update process...'

            # Give a short delay to ensure the application has exited
            sleep 1

            echo 'Replacing old executable...'
            rm -f {EnvHelper.SingleQuotePath(fileSystem.Path.Combine(currentFolder, originalFileName))}
            mv {EnvHelper.SingleQuotePath(fileSystem.Path.Combine(currentFolder, newFileName))} {EnvHelper.SingleQuotePath(
                fileSystem.Path.Combine(currentFolder, originalFileName)
            )}
            chmod +x {EnvHelper.SingleQuotePath(fileSystem.Path.Combine(currentFolder, originalFileName))}

            echo 'Starting the updated application...'
            nohup {EnvHelper.SingleQuotePath(fileSystem.Path.Combine(currentFolder, originalFileName))} > /dev/null 2>&1 &

            echo 'Cleaning up...'
            rm -- {EnvHelper.SingleQuotePath(scriptFilePath)}

            echo 'Update completed successfully.'
            
            """;
        fileSystem.File.WriteAllText(scriptFilePath, scriptContent);

        // Ensure the script is executable.
        var chmodResult = TryCatch(
            () =>
                Process
                    .Start(
                        new ProcessStartInfo
                        {
                            FileName = "/usr/bin/env",
                            ArgumentList = { "chmod", "+x", "--", scriptFilePath },
                            CreateNoWindow = true,
                            UseShellExecute = false,
                        }
                    )
                    ?.WaitForExit(),
            errorMessage: "Failed to set execute permission for the update script."
        );

        if (chmodResult.IsFailure)
            return chmodResult;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/env",
            ArgumentList = { "sh", "--", scriptFilePath },
            CreateNoWindow = false,
            UseShellExecute = false,
            WorkingDirectory = currentFolder,
        };

        return TryCatch(() => Process.Start(processStartInfo), errorMessage: "Failed to execute the update script.");
    }
}
