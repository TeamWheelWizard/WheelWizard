namespace WheelWizard.CloudSync.Backup;

public interface IProfileBackupService
{
    Task<BackupInfo> CreateBackupAsync();
    Task RestoreBackupAsync(BackupInfo backup);
}
