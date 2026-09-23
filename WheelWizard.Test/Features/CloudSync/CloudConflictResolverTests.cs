using WheelWizard.CloudSync;
using WheelWizard.CloudSync.Conflict;

namespace WheelWizard.Test.Features.CloudSync;

public sealed class CloudConflictResolverTests
{
    private readonly CloudConflictResolver _resolver = new();

    [Fact]
    public async Task CompareAsync_IndependentChanges_ReturnsConflict()
    {
        var local = new CloudSyncSnapshot(Manifest(3, "local"), 2, "base");
        var remote = new CloudSyncSnapshot(Manifest(3, "remote"), 2, "base");

        var result = await _resolver.CompareAsync(local, remote);

        Assert.Equal(ConflictKind.Conflict, result.Kind);
    }

    [Fact]
    public async Task CompareAsync_NewLinkedDevice_PullsSelectedCloudProfile()
    {
        var local = new CloudSyncSnapshot(Manifest(0, "new-device"), 0, string.Empty);
        var remote = new CloudSyncSnapshot(Manifest(7, "cloud"), 0, string.Empty);

        var result = await _resolver.CompareAsync(local, remote);

        Assert.Equal(ConflictKind.SafePull, result.Kind);
    }

    [Fact]
    public async Task CompareAsync_UnchangedRemoteWithLocalChange_PushesSafely()
    {
        var local = new CloudSyncSnapshot(Manifest(2, "local-change"), 2, "base");
        var remote = new CloudSyncSnapshot(Manifest(2, "base"), 2, "base");

        var result = await _resolver.CompareAsync(local, remote);

        Assert.Equal(ConflictKind.SafePush, result.Kind);
    }

    private static CloudProfileManifest Manifest(long revision, string hash) =>
        new()
        {
            ProfileId = Guid.NewGuid(),
            Revision = revision,
            ContentHash = hash,
        };
}
