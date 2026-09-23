using System.Text.Json;
using WheelWizard.Services;

namespace WheelWizard.CloudSync.ProfileLibrary;

public sealed class ProfileCloudBindingService : IProfileCloudBindingService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Path => System.IO.Path.Combine(PathManager.CloudSyncStateFolderPath, "local-profile-cloud-bindings.json");

    public async Task<Guid> GetProfileIdAsync(int localSlot)
    {
        if (localSlot is < 0 or >= 4)
            throw new ArgumentOutOfRangeException(nameof(localSlot));
        await _gate.WaitAsync();
        try
        {
            var bindings = await ReadAsync();
            if (bindings.TryGetValue(localSlot, out var profileId) && profileId != Guid.Empty)
                return profileId;

            profileId = Guid.NewGuid();
            bindings[localSlot] = profileId;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            await File.WriteAllTextAsync(Path, JsonSerializer.Serialize(bindings));
            return profileId;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<int, Guid>> ReadAsync()
    {
        if (!File.Exists(Path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, Guid>>(await File.ReadAllTextAsync(Path)) ?? [];
        }
        catch (JsonException)
        {
            // A malformed binding file must never make us repurpose an existing cloud profile.
            return [];
        }
    }
}
