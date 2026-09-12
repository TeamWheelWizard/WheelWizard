using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Shared;
using WheelWizard.Views.ModManagement;

namespace WheelWizard.Test.Views;

public class ModPreviewViewModelTests
{
    [Fact]
    public async Task LocalMod_DoesNotRequestNetworkPreview()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        using var model = new ModPreviewViewModel(-1, mods, media);

        await model.LoadAsync();

        Assert.True(model.ShowPlaceholder);
        Assert.Empty(mods.ReceivedCalls());
        Assert.Empty(media.ReceivedCalls());
    }

    [Fact]
    public async Task ConcurrentLoads_ShareOneRequest_AndFailureKeepsPlaceholder()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        var response = new TaskCompletionSource<OperationResult<GameBananaModDetails>>();
        mods.GetModDetails(42).Returns(response.Task);
        using var model = new ModPreviewViewModel(42, mods, media);

        var first = model.LoadAsync();
        var second = model.LoadAsync();
        response.SetResult(Fail("offline"));
        await Task.WhenAll(first, second);

        await mods.Received(1).GetModDetails(42);
        Assert.True(model.ShowPlaceholder);
        Assert.Empty(media.ReceivedCalls());
    }

    [Fact]
    public async Task DisposingPage_CancelsWaitingForDetails_AndRejectsFurtherLoads()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        mods.GetModDetails(42).Returns(new TaskCompletionSource<OperationResult<GameBananaModDetails>>().Task);
        var model = new ModPreviewViewModel(42, mods, media);
        var pending = model.LoadAsync();

        model.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        await model.LoadAsync();

        await mods.Received(1).GetModDetails(42);
        Assert.Empty(media.ReceivedCalls());
        Assert.Null(model.Image);
    }
}
