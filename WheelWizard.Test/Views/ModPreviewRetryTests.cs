using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Shared;
using WheelWizard.Views.ModManagement;

namespace WheelWizard.Test.Views;

public class ModPreviewRetryTests
{
    static ModPreviewRetryTests() => Avalonia.Skia.SkiaPlatform.Initialize();

    [Theory]
    [InlineData("details", false)]
    [InlineData("details", true)]
    [InlineData("image", false)]
    [InlineData("image", true)]
    [InlineData("corrupt", false)]
    [InlineData("corrupt", true)]
    public async Task FailedPreview_CanRetryAfterFailure(string stage, bool asynchronous)
    {
        using var fixture = new Fixture();
        var detailsFailure = new TaskCompletionSource<OperationResult<GameBananaModDetails>>();
        var imageFailure = new TaskCompletionSource<OperationResult<byte[]>>();
        OperationResult<byte[]> failedImage = stage == "corrupt" ? Ok(Encoding.UTF8.GetBytes("truncated image")) : Fail("offline");

        if (stage == "details")
            fixture
                .Mods.GetModDetails(42)
                .Returns(
                    asynchronous ? detailsFailure.Task : Task.FromResult<OperationResult<GameBananaModDetails>>(Fail("offline")),
                    Task.FromResult(Ok(Details()))
                );
        else
            fixture
                .Media.GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(asynchronous ? imageFailure.Task : Task.FromResult(failedImage), Task.FromResult(Ok(ImageBytes())));

        var first = fixture.Model.LoadAsync();
        if (asynchronous)
        {
            Assert.Same(first, fixture.Model.LoadAsync());
            if (stage == "details")
                detailsFailure.SetResult(Fail("offline"));
            else
                imageFailure.SetResult(failedImage);
        }
        await first;
        Assert.True(fixture.Model.ShowPlaceholder);

        await fixture.Model.LoadAsync();

        Assert.NotNull(fixture.Model.Image);
        Assert.False(fixture.Model.ShowPlaceholder);
        Assert.Equal(2, fixture.Model.Image.PixelSize.Width);
        Assert.Equal(3, fixture.Model.Image.PixelSize.Height);
        Assert.Equal(1, fixture.Notifications);
        await fixture.Mods.Received(2).GetModDetails(42);
        await fixture.Media.Received(stage == "details" ? 1 : 2).GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // A successful retry stays cached even when another card is created.
        var image = fixture.Model.Image;
        await fixture.Model.LoadAsync();
        Assert.Same(image, fixture.Model.Image);
        await fixture.Mods.Received(2).GetModDetails(42);
    }

    [Fact]
    public async Task OverlappingRetries_ShareOneActiveRequest()
    {
        using var fixture = new Fixture();
        var retry = new TaskCompletionSource<OperationResult<GameBananaModDetails>>();
        fixture.Mods.GetModDetails(42).Returns(Task.FromResult<OperationResult<GameBananaModDetails>>(Fail("offline")), retry.Task);
        await fixture.Model.LoadAsync();

        var firstRetry = fixture.Model.LoadAsync();
        var secondRetry = fixture.Model.LoadAsync();

        Assert.Same(firstRetry, secondRetry);
        retry.SetResult(Ok(Details()));
        await Task.WhenAll(firstRetry, secondRetry);
        Assert.NotNull(fixture.Model.Image);
        await fixture.Mods.Received(2).GetModDetails(42);
        await fixture.Media.Received(1).GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposingDuringRetry_PreventsFurtherRequestsOrImages()
    {
        using var fixture = new Fixture();
        var retry = new TaskCompletionSource<OperationResult<GameBananaModDetails>>();
        fixture.Mods.GetModDetails(42).Returns(Task.FromResult<OperationResult<GameBananaModDetails>>(Fail("offline")), retry.Task);
        await fixture.Model.LoadAsync();
        var loading = fixture.Model.LoadAsync();

        fixture.Model.Dispose();

        await loading.WaitAsync(TimeSpan.FromSeconds(1));
        retry.SetResult(Ok(Details()));
        await fixture.Model.LoadAsync();
        Assert.Null(fixture.Model.Image);
        Assert.Equal(0, fixture.Notifications);
        await fixture.Mods.Received(2).GetModDetails(42);
        Assert.Empty(fixture.Media.ReceivedCalls());
    }

    private static byte[] ImageBytes()
    {
        using var image = new Image<Rgba32>(2, 3);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static GameBananaModDetails Details() =>
        new()
        {
            Id = 42,
            Name = "mod",
            Version = "1",
            ProfileUrl = "https://example.test/mod",
            LikeCount = 0,
            ViewCount = 0,
            DateAdded = 0,
            DateModified = 0,
            IsObsolete = false,
            Author = null!,
            Game = null!,
            Category = null!,
            Text = "",
            License = "",
            DownloadCount = 0,
            PreviewMedia = new()
            {
                Images =
                [
                    new()
                    {
                        Type = "screenshot",
                        BaseUrl = "https://example.test/images",
                        File = "full.png",
                        File220 = "small.png",
                    },
                ],
            },
        };

    private sealed class Fixture : IDisposable
    {
        public IGameBananaSingletonService Mods { get; } = Substitute.For<IGameBananaSingletonService>();
        public IGameBananaMediaService Media { get; } = Substitute.For<IGameBananaMediaService>();
        public ModPreviewViewModel Model { get; }
        public int Notifications { get; private set; }

        public Fixture()
        {
            Mods.GetModDetails(42).Returns(Ok(Details()));
            Media.GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Ok(ImageBytes()));
            Model = new(42, Mods, Media);
            Model.PropertyChanged += (_, _) => Notifications++;
        }

        public void Dispose() => Model.Dispose();
    }
}
