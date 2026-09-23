using System.Text.Json;
using WheelWizard.Services;

namespace WheelWizard.CloudSync.ProfileLibrary;

public sealed class ProfileCloudBindingService : IProfileCloudBindingService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Path => System.IO.Path.Combine(PathManager.CloudSyncStateFolderPath, "local-profile-cloud-bindings.json");

    public async Task<Guid> GetProfileIdAsync(int localSlot, string licenseIdentity)
    {
        if (localSlot is < 0 or >= 4)
            throw new ArgumentOutOfRangeException(nameof(localSlot));
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseIdentity);
        await _gate.WaitAsync();
        try
        {
            var bindings = await ReadAsync();
            if (
                bindings.TryGetValue(localSlot, out var binding)
                && binding.LicenseIdentity == licenseIdentity
                && binding.ProfileId != Guid.Empty
            )
                return binding.ProfileId;

            var profileId = Guid.NewGuid();
            bindings[localSlot] = new LocalProfileBinding(licenseIdentity, profileId);
            await WriteAsync(bindings);
            return profileId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BindAsync(int localSlot, string licenseIdentity, Guid profileId)
    {
        if (localSlot is < 0 or >= 4)
            throw new ArgumentOutOfRangeException(nameof(localSlot));
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseIdentity);
        if (profileId == Guid.Empty)
            throw new ArgumentException("A cloud profile binding requires a profile ID.", nameof(profileId));

        await _gate.WaitAsync();
        try
        {
            var bindings = await ReadAsync();
            if (
                bindings.TryGetValue(localSlot, out var current)
                && current.ProfileId == profileId
                && current.LicenseIdentity == licenseIdentity
            )
                return;
            bindings[localSlot] = new LocalProfileBinding(licenseIdentity, profileId);
            await WriteAsync(bindings);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<int, LocalProfileBinding>> ReadAsync()
    {
        if (!File.Exists(Path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, LocalProfileBinding>>(await File.ReadAllTextAsync(Path)) ?? [];
        }
        catch (JsonException)
        {
            // A malformed binding file must never make us repurpose an existing cloud profile.
            return [];
        }
    }

    private async Task WriteAsync(Dictionary<int, LocalProfileBinding> bindings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(bindings));
        File.Move(temporary, Path, overwrite: true);
    }

    private sealed record LocalProfileBinding(string LicenseIdentity, Guid ProfileId);
}
