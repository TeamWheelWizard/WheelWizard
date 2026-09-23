using System.Text.Json;
using WheelWizard.Services;
using WheelWizard.Settings;

namespace WheelWizard.CloudSync.Enrollment;

/// <summary>
/// A local safety latch, not a Retro WFC integration.  It never changes server data or console
/// identity; only a successful, independently observed login may mark a device verified.
/// </summary>
public sealed class RetroWfcEnrollmentService(ISettingsManager settings) : IRetroWfcEnrollmentService
{
    public async Task<EnrollmentState> GetStateAsync() => (await ReadAsync()).EnrollmentState;

    public async Task<ProbeResult> ProbeOnlineProfileAsync()
    {
        // 22005 is a server-side identity/linking condition.  Detecting it only blocks uploads;
        // it is never an instruction to copy a serial, device ID, or NAND.
        var logsRoot = PathManager.RecompFolderPath;
        if (
            Directory.Exists(logsRoot)
            && Directory
                .EnumerateFiles(logsRoot, "*.log", SearchOption.AllDirectories)
                .Take(100)
                .Any(file => new FileInfo(file).Length < 10_000_000 && File.ReadAllText(file).Contains("22005", StringComparison.Ordinal))
        )
        {
            await WriteAsync((await ReadAsync()) with { EnrollmentState = EnrollmentState.Blocked });
            return new ProbeResult(
                false,
                true,
                "Retro WFC reported Error 22005. Upload remains blocked; use the official linking/support process."
            );
        }

        return new ProbeResult(false, false, "No verified online-login evidence is available yet; upload remains blocked.");
    }

    public async Task MarkVerifiedAsync()
    {
        var current = await ReadAsync();
        if (current.EnrollmentState == EnrollmentState.Blocked)
            throw new InvalidOperationException("A blocked device cannot be verified until the official linking issue is resolved.");
        await WriteAsync(current with { EnrollmentState = EnrollmentState.Verified });
    }

    private string StatePath => Path.Combine(PathManager.CloudSyncStateFolderPath, $"enrollment-{ProfileId:D}-{DeviceId:D}.json");
    private Guid ProfileId => Guid.TryParse(settings.Get<string>(settings.CLOUD_PROFILE_ID), out var id) ? id : Guid.Empty;
    private Guid DeviceId => Guid.TryParse(settings.Get<string>(settings.CLOUD_DEVICE_ID), out var id) ? id : Guid.Empty;

    private async Task<CloudSyncLocalState> ReadAsync()
    {
        if (!File.Exists(StatePath))
            return new CloudSyncLocalState();
        return JsonSerializer.Deserialize<CloudSyncLocalState>(await File.ReadAllTextAsync(StatePath)) ?? new CloudSyncLocalState();
    }

    private async Task WriteAsync(CloudSyncLocalState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(state));
    }
}
