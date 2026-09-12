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

    [Fact]
    public async Task SearchPreview_UsesKnownUrlWithoutFetchingModDetails()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        media.GetImageAsync("https://images.test/preview.png", Arg.Any<CancellationToken>()).Returns(Fail("offline"));
        using var model = new ModPreviewViewModel(42, mods, media, "https://images.test/preview.png");

        await model.LoadAsync();
        await model.LoadAsync();

        Assert.Empty(mods.ReceivedCalls());
        await media.Received(1).GetImageAsync("https://images.test/preview.png", Arg.Any<CancellationToken>());
        Assert.True(model.ShowPlaceholder);
    }

    [Fact]
    public async Task SearchWithoutImage_DoesNotRequestDetailsOrMedia()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        using var model = new ModPreviewViewModel(42, mods, media, "");

        await model.LoadAsync();

        Assert.Empty(mods.ReceivedCalls());
        Assert.Empty(media.ReceivedCalls());
    }

    [Fact]
    public async Task DisposingSearchResult_CancelsWaitingForMedia_AndIgnoresLateResponse()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        var response = new TaskCompletionSource<OperationResult<byte[]>>();
        CancellationToken requestToken = default;
        media
            .GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requestToken = call.Arg<CancellationToken>();
                return response.Task;
            });
        var model = new ModPreviewViewModel(42, mods, media, "https://images.test/preview.png");
        var pending = model.LoadAsync();
        model.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        response.SetResult(new byte[] { 1, 2, 3 });

        Assert.True(requestToken.IsCancellationRequested);
        Assert.Null(model.Image);
        await model.LoadAsync();
        await media.Received(1).GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
