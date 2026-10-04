namespace WheelWizard.CustomDistributions;

public interface IDistributionPrompts
{
    Task<bool> ConfirmOldSaveBackupAsync();
    Task<string?> RequestBetaPasswordAsync();
    Task<bool> ConfirmPasswordRetryAsync();
}
