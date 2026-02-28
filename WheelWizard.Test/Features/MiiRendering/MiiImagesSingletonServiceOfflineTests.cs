using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared;
using WheelWizard.Utilities.Mockers;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features.MiiRendering;

public class MiiImagesSingletonServiceOfflineTests
{
    [Fact]
    public async Task GetImageAsync_ShouldRenderOnlyOnce_ForConcurrentDuplicateRequests()
    {
        var renderer = new CountingNativeRenderer();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MiiImagesSingletonService(renderer, cache, NullLogger<MiiImagesSingletonService>.Instance);

        var mii = new MiiFactory().Create();
        var specifications = new MiiImageSpecifications
        {
            Name = "Concurrent",
            Size = MiiImageSpecifications.ImageSize.small,
            ExpirationSeconds = TimeSpan.FromMinutes(10),
        };

        var firstTask = service.GetImageAsync(mii, specifications);
        var secondTask = service.GetImageAsync(mii, specifications);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, renderer.CallCount);
        Assert.All(results, r => Assert.True(r.IsFailure));
    }

    [Fact]
    public async Task GetImageAsync_ShouldFail_WhenMiiIsNull()
    {
        var renderer = new CountingNativeRenderer();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MiiImagesSingletonService(renderer, cache, NullLogger<MiiImagesSingletonService>.Instance);

        var result = await service.GetImageAsync(null, new MiiImageSpecifications());

        Assert.True(result.IsFailure);
        Assert.Contains("Mii cannot be null", result.Error!.Message);
        Assert.Equal(0, renderer.CallCount);
    }

    private sealed class CountingNativeRenderer : IMiiNativeRenderer
    {
        private int _callCount;

        public int CallCount => _callCount;

        public async Task<OperationResult<Avalonia.Media.Imaging.Bitmap>> RenderAsync(
            Mii mii,
            string studioData,
            MiiImageSpecifications specifications,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(60, cancellationToken);
            return Fail("Synthetic renderer failure for headless tests.");
        }
    }
}
