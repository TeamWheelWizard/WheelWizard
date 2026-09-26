namespace WheelWizard.CloudSync.Backup;

public interface IProfileBackupService
{
    Task<BackupInfo> CreateBackupAsync(string? rksysPath = null);
    Task RestoreBackupAsync(BackupInfo backup);
}
