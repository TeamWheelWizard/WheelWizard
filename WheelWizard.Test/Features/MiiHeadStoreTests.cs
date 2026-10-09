using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.Test.Features;

public class MiiHeadStoreTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static List<HeadMeshData> Head() =>
        [
            new()
            {
                Positions = [default],
                Normals = [default],
                Tangents = [default],
                Texcoords = [default],
                Parameters = [default],
                Indices = [0, 0, 0],
                CullMode = 0,
                ModulateMode = 0,
                ColorR = default,
                ColorG = default,
                ColorB = default,
                HasTangent = false,
                Ambient = default,
                Diffuse = default,
                Specular = default,
                SpecularPower = 0,
                SpecularMode = 0,
                RimColor = default,
            },
        ];

    private static Task<IReadOnlyList<HeadMeshData>?> RequestHead(MiiHeadStore store, string studio, Func<bool>? wanted = null)
    {
        var done = new TaskCompletionSource<IReadOnlyList<HeadMeshData>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.RequestHead(studio, MiiHeadDetail.Small, wanted ?? (() => true), head => done.SetResult(head));
        return done.Task;
    }

    [Fact]
    public async Task Heads_AreBuiltOnceAndSharedByEveryone()
    {
        var renderer = Substitute.For<IMiiNativeRenderer>();
        var gate = new ManualResetEventSlim();
        renderer
            .BuildHeadModel("mii", 0, MiiHeadDetail.Small)
            .Returns(_ =>
            {
                gate.Wait(Timeout);
                return Head();
            });
        var store = MiiHeadStore.For(renderer);

        var first = RequestHead(store, "mii");
        var second = RequestHead(store, "mii");
        gate.Set();
        var heads = await Task.WhenAll(first, second).WaitAsync(Timeout);

        Assert.NotNull(heads[0]);
        Assert.Same(heads[0], heads[1]);
        Assert.True(store.TryGetHead("mii", MiiHeadDetail.Small, out var stored));
        Assert.Same(heads[0], stored);
        Assert.Same(heads[0], await RequestHead(store, "mii").WaitAsync(Timeout));
        renderer.Received(1).BuildHeadModel("mii", 0, MiiHeadDetail.Small);
        Assert.False(store.TryGetHead("mii", MiiHeadDetail.Full, out _));
    }

    [Fact]
    public async Task Heads_NobodyWantsAnyMore_AreNotBuilt()
    {
        var renderer = Substitute.For<IMiiNativeRenderer>();
        renderer.BuildHeadModel(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<MiiHeadDetail>()).Returns(_ => Head());
        var store = MiiHeadStore.For(renderer);

        // E.g. a card that was scrolled past before its turn came.
        Assert.Null(await RequestHead(store, "gone", () => false).WaitAsync(Timeout));
        renderer.DidNotReceive().BuildHeadModel("gone", Arg.Any<int>(), Arg.Any<MiiHeadDetail>());
        Assert.NotNull(await RequestHead(store, "gone").WaitAsync(Timeout));
    }

    [Fact]
    public async Task Masks_AreStoredPerExpressionAndDetail()
    {
        var renderer = Substitute.For<IMiiNativeRenderer>();
        renderer
            .BuildMaskLayer("mii", 5, MiiMaskLayers.All, MiiHeadDetail.Small)
            .Returns(new MiiMaskLayerTexture(new byte[256 * 256 * 4], 256, 256));
        var store = MiiHeadStore.For(renderer);
        var done = new TaskCompletionSource<MiiMaskLayerTexture?>(TaskCreationOptions.RunContinuationsAsynchronously);

        store.RequestMask("mii", 5, MiiMaskLayers.All, MiiHeadDetail.Small, () => true, done.SetResult);

        Assert.NotNull(await done.Task.WaitAsync(Timeout));
        Assert.True(store.TryGetMask("mii", 5, MiiMaskLayers.All, MiiHeadDetail.Small, out _));
        Assert.False(store.TryGetMask("mii", 1, MiiMaskLayers.All, MiiHeadDetail.Small, out _));
        Assert.False(store.TryGetMask("mii", 5, MiiMaskLayers.All, MiiHeadDetail.Full, out _));
    }

    [Fact]
    public async Task Workers_RunWorkAndSkipCancelledWork()
    {
        Assert.Equal(42, await MiiRenderWorkers.Run(() => 42).WaitAsync(Timeout));

        var ran = false;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                MiiRenderWorkers
                    .Run(
                        () =>
                        {
                            ran = true;
                            return 0;
                        },
                        cancelled.Token
                    )
                    .WaitAsync(Timeout)
        );
        Assert.False(ran);
    }
}
