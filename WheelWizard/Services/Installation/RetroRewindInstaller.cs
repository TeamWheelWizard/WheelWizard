using System.IO.Compression;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Installation;

public static class RetroRewindInstaller
{
    
    public static bool IsRetroRewindInstalled() => File.Exists(PathManager.RetroRewindVersionFile);

    public static string CurrentRRVersion()
    {
        var versionFilePath = PathManager.RetroRewindVersionFile;
        return IsRetroRewindInstalled() ? File.ReadAllText(versionFilePath).Trim() : "Not Installed";
    }

    public static async Task<bool> HandleNotInstalled()
    {
        var result = await new YesNoWindow()
            .SetMainText(Phrases.PopupText_RRNotDeterment)
            .SetExtraText(Phrases.PopupText_DownloadRR)
            .AwaitAnswer();

        if (!result) return false;

        await InstallRetroRewind();
        return true;
    }

    public static async Task<bool> HandleOldVersion()
    {
        var result = await new YesNoWindow()
            .SetMainText(Phrases.PopupText_RRToOld)
            .SetExtraText(Phrases.PopupText_ReinstallRR)
            .AwaitAnswer();

        if (!result) return false;

        await InstallRetroRewind();
        return true;
    }

    public static async Task InstallRetroRewind()
    {
        if (IsRetroRewindInstalled())
            DeleteExistingRetroRewind();

        if (hasOldrksys())
        {
            var rksysQuestion = new YesNoWindow()
                                .SetMainText(Phrases.PopupText_OldRksysFound)
                                .SetExtraText(Phrases.PopupText_OldRksysFoundExplained);
            if (await rksysQuestion.AwaitAnswer()) 
                await backupOldrksys();

        }
        var serverResponse = await HttpClientHelper.GetAsync<string>(Endpoints.RRUrl);
        if (!serverResponse.Succeeded)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Could not connect to the server")
                .SetInfoText(Phrases.PopupText_CouldNotConnectServer).ShowDialog();
            return;
        }
        await DownloadAndExtractRetroRewind(PathManager.RetroRewindTempFile);
        await RetroRewindUpdater.UpdateRR();
    }

    public static async Task ReinstallRR()
    {
        var result = await new YesNoWindow()
            .SetMainText(Phrases.PopupText_ReinstallRR)
            .SetExtraText(Phrases.PopupText_ReinstallQuestion)
            .AwaitAnswer();
        
        if (!result) 
            return;
        
        DeleteExistingRetroRewind();
        await InstallRetroRewind();
    }
    private static async Task DownloadAndExtractRetroRewind(string tempZipPath)
    {
        var progressWindow = new ProgressWindow(Phrases.PopupText_InstallingRR);
        progressWindow.SetExtraText(Phrases.PopupText_InstallingRRFirstTime);
        progressWindow.Show();

        try
        {
            await DownloadHelper.DownloadToLocationAsync(Endpoints.RRZipUrl, tempZipPath, progressWindow);
            progressWindow.SetExtraText(Common.State_Extracting);
            var extractionPath = PathManager.RiivolutionWhWzFolderPath;
            ZipFile.ExtractToDirectory(tempZipPath, extractionPath, true);
        }
        finally
        {
            progressWindow.Close();
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);
        }
    }

    private static bool hasOldrksys()
    {
        var rrWfcPaths = new[]
        {
            Path.Combine(PathManager.SaveFolderPath),
            Path.Combine(PathManager.LoadFolderPath, "Riivolution", "save", "RetroWFC")
        };

        foreach (var rrWfc in rrWfcPaths)
        {
            if (!Directory.Exists(rrWfc)) continue;
            var rksysFiles = Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
            if (rksysFiles.Length > 0)
                return true;
        }

        return false;
    }

    private static async Task backupOldrksys()
    {
        var rrWfc = Path.Combine(PathManager.SaveFolderPath);
        if (!Directory.Exists(rrWfc)) return;
        var rksysFiles = Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
        if (rksysFiles.Length == 0) return;
        var sourceFile = rksysFiles[0];
        var regionFolder = Path.GetDirectoryName(sourceFile);
        var regionFolderName = Path.GetFileName(regionFolder);
        var datFileData = await File.ReadAllBytesAsync(sourceFile);
        if (regionFolderName == null) return;
        var destinationFolder = Path.Combine(PathManager.SaveFolderPath, regionFolderName);
        Directory.CreateDirectory(destinationFolder);
        var destinationFile = Path.Combine(destinationFolder, "rksys.dat");
        await File.WriteAllBytesAsync(destinationFile, datFileData);
        
    }

    private static void DeleteExistingRetroRewind()
    {
        var retroRewindPath = PathManager.RetroRewind6FolderPath;
        if (Directory.Exists(retroRewindPath))
            Directory.Delete(retroRewindPath, true);
    }
}
