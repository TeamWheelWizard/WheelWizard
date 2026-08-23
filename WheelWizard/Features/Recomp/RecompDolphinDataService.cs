using System.IO.Abstractions;
using WheelWizard.Services;
using WheelWizard.Settings;

namespace WheelWizard.Recomp;

public interface IRecompDolphinDataService
{
    bool IsEnabled { get; }

    /// <summary>Whether the recomp uses a copy of the Dolphin NAND instead of sharing it in place.</summary>
    bool CopyEnabled { get; }

    string? LinkedUserFolderPath { get; }

    /// <summary>The NAND directory handed to the recomp, honoring both the sharing and the copy choice.</summary>
    string? NandFolderPath { get; }

    string? SourceNandFolderPath { get; }

    string? FindCandidateUserFolder();
    OperationResult Link(string userFolderPath);
    void SetEnabled(bool enabled);
    void SetCopyEnabled(bool enabled);

    OperationResult CopyNandForRecomp();

    OperationResult ApplyNandToRecompConfig();
}

public sealed class RecompDolphinDataService(ISettingsManager settings, IRecompSettingManager recompSettings, IFileSystem fileSystem)
    : IRecompDolphinDataService
{
    public bool IsEnabled => settings.Get<bool>(settings.RECOMP_USE_DOLPHIN_DATA);

    public bool CopyEnabled => settings.Get<bool>(settings.RECOMP_COPY_DOLPHIN_NAND);

    public string? LinkedUserFolderPath => ValidateUserFolder(settings.Get<string>(settings.USER_FOLDER_PATH));

    public string? NandFolderPath
    {
        get
        {
            if (!IsEnabled)
                return null;

            // A missing copy never falls back to the Dolphin NAND: the user chose the copy exactly
            // so that Dolphin's own data is left alone, and a private NAND is the safe default.
            if (CopyEnabled)
                return ValidateNandFolder(PathManager.RecompNandCopyFolderPath);

            return SourceNandFolderPath;
        }
    }

    public string? SourceNandFolderPath
    {
        get
        {
            // Dolphin can redirect its NAND away from <UserFolder>\Wii. Wheel Wizard already reads
            // that effective value from Dolphin.ini, so hand the same directory to WiiCompiled.
            // Falling back to the user folder covers Dolphin's default and portable layouts.
            var configuredNand = ValidateNandFolder(settings.Get<string>(settings.NAND_ROOT_PATH));
            if (configuredNand is not null)
                return configuredNand;

            var userFolder = LinkedUserFolderPath ?? FindCandidateUserFolder();
            return userFolder is null ? null : fileSystem.Path.Combine(userFolder, "Wii");
        }
    }

    public string? FindCandidateUserFolder() => ValidateUserFolder(PathManager.TryFindUserFolderPath());

    public OperationResult Link(string userFolderPath)
    {
        var validated = ValidateUserFolder(userFolderPath);
        if (validated is null)
            return Fail("The selected Dolphin folder does not contain a Wii data folder.");

        if (!settings.Set(settings.USER_FOLDER_PATH, validated))
            return Fail("Wheel Wizard could not save the Dolphin user folder.");

        settings.Set(settings.RECOMP_USE_DOLPHIN_DATA, true);
        return Ok();
    }

    public void SetEnabled(bool enabled) => settings.Set(settings.RECOMP_USE_DOLPHIN_DATA, enabled);

    public void SetCopyEnabled(bool enabled) => settings.Set(settings.RECOMP_COPY_DOLPHIN_NAND, enabled);

    public OperationResult CopyNandForRecomp()
    {
        var source = SourceNandFolderPath;
        if (source is null)
            return Fail("No Dolphin Wii data folder was found to copy.");

        var destination = PathManager.RecompNandCopyFolderPath;
        try
        {
            if (fileSystem.Directory.Exists(destination))
                fileSystem.Directory.Delete(destination, recursive: true);
            CopyDirectory(source, destination);
        }
        catch (Exception)
        {
            // A half-written copy must not survive: NandFolderPath would hand the recomp a NAND with
            // silently missing saves. Deleting it makes the failure loud and the state honest.
            try
            {
                if (fileSystem.Directory.Exists(destination))
                    fileSystem.Directory.Delete(destination, recursive: true);
            }
            catch
            {
                // The partial copy could not be removed either; the validation on read still guards it.
            }

            return Fail("Wheel Wizard could not copy the Dolphin Wii data folder.");
        }

        return Ok();
    }

    public OperationResult ApplyNandToRecompConfig()
    {
        var nandFolderPath = NandFolderPath?.TrimEnd('\\', '/');
        try
        {
            // The backend owns creating Config.toml, so reread first: right after an install the
            // file is brand new and this manager may never have seen it. Without a file there is
            // nothing to configure yet, and the next successful install applies this again.
            recompSettings.ReloadSettings();

            if (nandFolderPath is null)
            {
                recompSettings.RemoveTomlSetting("paths", "nand_root");
                settings.Set(settings.RECOMP_NAND_ROOT, "", skipSave: true);
                return Ok();
            }

            // Reset before assigning so the write happens even when this process already holds the
            // same value in memory - a reinstall recreates Config.toml without the key, and only
            // an actual change would otherwise be saved.
            settings.Set(settings.RECOMP_NAND_ROOT, "", skipSave: true);
            return settings.Set(settings.RECOMP_NAND_ROOT, nandFolderPath)
                ? Ok()
                : Fail("Wheel Wizard could not save the Wii data folder to the WiiCompiled configuration.");
        }
        catch (Exception exception)
        {
            return Fail($"Wheel Wizard could not update the WiiCompiled configuration: {exception.Message}");
        }
    }

    private void CopyDirectory(string sourceFolder, string destinationFolder)
    {
        fileSystem.Directory.CreateDirectory(destinationFolder);
        foreach (var file in fileSystem.Directory.GetFiles(sourceFolder))
        {
            fileSystem.File.Copy(file, fileSystem.Path.Combine(destinationFolder, fileSystem.Path.GetFileName(file)), overwrite: true);
        }

        foreach (var folder in fileSystem.Directory.GetDirectories(sourceFolder))
        {
            CopyDirectory(folder, fileSystem.Path.Combine(destinationFolder, fileSystem.Path.GetFileName(folder)));
        }
    }

    private string? ValidateUserFolder(string? userFolderPath)
    {
        if (string.IsNullOrWhiteSpace(userFolderPath))
            return null;

        try
        {
            var fullPath = fileSystem.Path.GetFullPath(userFolderPath);
            var nandPath = fileSystem.Path.Combine(fullPath, "Wii");
            return fileSystem.Directory.Exists(fullPath) && fileSystem.Directory.Exists(nandPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private string? ValidateNandFolder(string? nandFolderPath)
    {
        if (string.IsNullOrWhiteSpace(nandFolderPath))
            return null;

        try
        {
            var fullPath = fileSystem.Path.GetFullPath(nandFolderPath);
            return fileSystem.Directory.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }
}
