using WheelWizard.CustomDistributions;
using WheelWizard.Services;

namespace WheelWizard.CloudSync.Backup;

public sealed class ProfileBackupService(ICustomDistributionSingletonService distributions) : IProfileBackupService
{
    public Task<BackupInfo> CreateBackupAsync()
    {
        var now = DateTime.UtcNow;
        var target = Path.Combine(PathManager.CloudBackupFolderPath, now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(target);
        var nand = PathManager.GetActiveNandPath();
        var rksys = distributions.RetroRewind.FindExistingRksysPath();
        CopyIfPresent(rksys, Path.Combine(target, "MarioKart", "rksys.dat"));
        CopyIfPresent(PathManager.GetMiiDatabasePath(nand), Path.Combine(target, "Mii", "RFL_DB.dat"));
        CopyIfPresent(PathManager.GetRetroRewindRatingPath(nand), Path.Combine(target, "RetroRewind", "RRRating.pul"));
        CopyIfPresent(PathManager.GetRetroRewindSettingsPath(nand), Path.Combine(target, "RetroRewind", "RRSettings.pul"));
        CopyIfPresent(PathManager.GetRetroRewindGameSettingsPath(nand), Path.Combine(target, "RetroRewind", "RRGameSettings.pul"));
        return Task.FromResult(new BackupInfo(target, now));
    }

    public Task RestoreBackupAsync(BackupInfo backup)
    {
        var nand = PathManager.GetActiveNandPath();
        var rksys = distributions.RetroRewind.FindExistingRksysPath();
        if (rksys is not null)
            CopyIfPresent(Path.Combine(backup.FolderPath, "MarioKart", "rksys.dat"), rksys);
        CopyIfPresent(Path.Combine(backup.FolderPath, "Mii", "RFL_DB.dat"), PathManager.GetMiiDatabasePath(nand));
        CopyIfPresent(Path.Combine(backup.FolderPath, "RetroRewind", "RRRating.pul"), PathManager.GetRetroRewindRatingPath(nand));
        CopyIfPresent(Path.Combine(backup.FolderPath, "RetroRewind", "RRSettings.pul"), PathManager.GetRetroRewindSettingsPath(nand));
        CopyIfPresent(
            Path.Combine(backup.FolderPath, "RetroRewind", "RRGameSettings.pul"),
            PathManager.GetRetroRewindGameSettingsPath(nand)
        );
        return Task.CompletedTask;
    }

    private static void CopyIfPresent(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }
}
