using System.IO.Abstractions;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Recomp;

public interface IRecompDolphinDataService
{
    bool IsSharingEnabled { get; }

    /// <summary>Whether private mode uses a copy originally imported from Dolphin.</summary>
    bool CopyEnabled { get; }

    string? LinkedUserFolderPath { get; }

    /// <summary>The NAND directory handed to the recomp, honoring both the sharing and the copy choice.</summary>
    string? NandFolderPath { get; }

    string? SourceNandFolderPath { get; }

    string? FindCandidateUserFolder();
    OperationResult Link(string userFolderPath);
    void SetSharingEnabled(bool enabled);
    void SetCopyEnabled(bool enabled);

    OperationResult CopyNandForRecomp();

    /// <summary>
    /// Writes the NAND and Retro Rewind folders Wheel Wizard resolves today into the recomp's Config.toml.
    /// Both move (sharing toggles, a relocated data folder, the Retro Rewind migration), and the runtime only
    /// knows what this file says, so this runs before every launch as well as after each of those changes.
    /// </summary>
    OperationResult ApplyPathsToRecompConfig();
}

public sealed class RecompDolphinDataService(
    ISettingsManager settings,
    IRecompSettingManager recompSettings,
    IFileSystem fileSystem,
    IDolphinDiscoveryService dolphinDiscovery,
    IRecompPaths paths,
    ICustomDistributionPaths distributionPaths
) : IRecompDolphinDataService
{
    public bool IsSharingEnabled => settings.Get<bool>(settings.RECOMP_USE_DOLPHIN_DATA);

    public bool CopyEnabled => settings.Get<bool>(settings.RECOMP_COPY_DOLPHIN_NAND);

    public string? LinkedUserFolderPath => ValidateUserFolder(settings.Get<string>(settings.USER_FOLDER_PATH));

    public string? NandFolderPath
    {
        get
        {
            // Sharing always means Dolphin's live NAND, even when a private clone is kept for the
            // next time sharing is disabled.
            if (IsSharingEnabled)
                return SourceNandFolderPath;

            // A missing copy never falls back to the Dolphin NAND: the user chose the copy exactly
            // so that Dolphin's own data is left alone, and a private NAND is the safe default.
            if (CopyEnabled)
                return ValidateNandFolder(paths.NandCopyFolderPath);

            return null;
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

    public string? FindCandidateUserFolder() =>
        ValidateUserFolder(
            dolphinDiscovery.FindUserDirectory(
                settings.Get<string>(settings.DOLPHIN_LOCATION),
                settings.Get<string>(settings.USER_FOLDER_PATH)
            )
        );

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

    public void SetSharingEnabled(bool enabled) => settings.Set(settings.RECOMP_USE_DOLPHIN_DATA, enabled);

    public void SetCopyEnabled(bool enabled) => settings.Set(settings.RECOMP_COPY_DOLPHIN_NAND, enabled);

    public OperationResult CopyNandForRecomp()
    {
        var source = SourceNandFolderPath;
        if (source is null)
            return Fail("No Dolphin Wii data folder was found to copy.");

        var destination = paths.NandCopyFolderPath;
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

    public OperationResult ApplyPathsToRecompConfig()
    {
        var nandFolderPath = NandFolderPath?.TrimEnd('\\', '/');
        try
        {
            // The backend owns creating Config.toml, so reread first: right after an install the
            // file is brand new and this manager may never have seen it. Without a file there is
            // nothing to configure yet, and the next successful install applies this again.
            recompSettings.ReloadSettings(paths.ConfigFilePath);
            if (!fileSystem.File.Exists(paths.ConfigFilePath))
                return Ok();

            var nandResult = ApplyNandRoot(nandFolderPath);
            if (nandResult.IsFailure)
                return nandResult;

            return ApplyRetroRewindRoot();
        }
        catch (Exception exception)
        {
            return Fail($"Wheel Wizard could not update the WiiCompiled configuration: {exception.Message}");
        }
    }

    private OperationResult ApplyNandRoot(string? nandFolderPath)
    {
        if (nandFolderPath is null)
        {
            recompSettings.RemoveTomlSetting(paths.ConfigFilePath, "paths", "nand_root");
            settings.Set(settings.RECOMP_NAND_ROOT, "", skipSave: true);
            return Ok();
        }

        return ForceWrite(settings.RECOMP_NAND_ROOT, nandFolderPath)
            ? Ok()
            : Fail("Wheel Wizard could not save the Wii data folder to the WiiCompiled configuration.");
    }

    private OperationResult ApplyRetroRewindRoot()
    {
        // Without an installed Retro Rewind the setup's own value is the only one there is, so keep it.
        var retroRewindFolderPath = distributionPaths.RetroRewindFolderPath;
        if (!fileSystem.Directory.Exists(retroRewindFolderPath))
            return Ok();

        return ForceWrite(settings.RECOMP_RETRO_REWIND_ROOT, fileSystem.Path.GetFullPath(retroRewindFolderPath).TrimEnd('\\', '/'))
            ? Ok()
            : Fail("Wheel Wizard could not save the Retro Rewind folder to the WiiCompiled configuration.");
    }

    // Reset before assigning so the write happens even when this process already holds the
    // same value in memory - a reinstall recreates Config.toml without the key, and only
    // an actual change would otherwise be saved.
    private bool ForceWrite(Setting<string> setting, string value)
    {
        settings.Set(setting, "", skipSave: true);
        return settings.Set(setting, value);
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

/// <summary>Where a fresh WiiCompiled install takes its Wii data from.</summary>
public enum RecompNandChoice
{
    /// <summary>The runtime's own NAND; Dolphin's data is left alone.</summary>
    Private,

    /// <summary>Dolphin's live NAND, shared in place.</summary>
    Share,

    /// <summary>A Wheel Wizard-owned copy of Dolphin's NAND.</summary>
    Copy,
}

public static class RecompNandChoices
{
    /// <summary>
    /// Applies a fresh install's NAND choice. The launcher asks it in popups and the onboarding inline, so both
    /// share this one definition of what each answer means.
    /// </summary>
    public static OperationResult ApplyNandChoice(this IRecompDolphinDataService data, RecompNandChoice choice)
    {
        switch (choice)
        {
            case RecompNandChoice.Share:
                data.SetCopyEnabled(false);
                data.SetSharingEnabled(true);
                return Ok();
            case RecompNandChoice.Copy:
                var copyResult = data.CopyNandForRecomp();
                if (copyResult.IsFailure)
                    return copyResult;

                // Flipped only after the copy durably exists, so a failed copy can never leave the settings
                // pointing at a NAND that is not there.
                data.SetCopyEnabled(true);
                data.SetSharingEnabled(false);
                return Ok();
            default:
                data.SetSharingEnabled(false);
                data.SetCopyEnabled(false);
                return Ok();
        }
    }
}
