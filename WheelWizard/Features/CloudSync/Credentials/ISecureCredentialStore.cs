namespace WheelWizard.CloudSync.Credentials;

public sealed record Secret(string Value);

public interface ISecureCredentialStore
{
    Task SaveAsync(string key, Secret value);
    Task<Secret?> GetAsync(string key);
    Task DeleteAsync(string key);
}
