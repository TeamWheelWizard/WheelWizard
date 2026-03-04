using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages;

public interface IMiiImagesSingletonService
{
    Task<OperationResult<Bitmap>> GetImageAsync(Mii? mii, MiiImageSpecifications specifications);
}

public class MiiImagesSingletonService(IMiiNativeRenderer nativeRenderer, ILogger<MiiImagesSingletonService> logger)
    : IMiiImagesSingletonService,
        IDisposable
{
    private const long ImageCacheSizeLimitBytes = 64L * 1024L * 1024L;
    private readonly MemoryCache _imageCache = new(
        new MemoryCacheOptions { SizeLimit = ImageCacheSizeLimitBytes, CompactionPercentage = 0.2 }
    );

    // Track in-flight requests to prevent duplicate renders.
    private readonly ConcurrentDictionary<string, Task<OperationResult<Bitmap>>> _inFlightRequests = new();

    public async Task<OperationResult<Bitmap>> GetImageAsync(Mii? mii, MiiImageSpecifications specifications)
    {
        if (mii == null)
            return Fail("Mii cannot be null.");

        var data = MiiStudioDataSerializer.Serialize(mii);
        if (data.IsFailure)
        {
            logger.LogWarning(
                "Mii studio serialization failed for image '{ImageName}' ({BodyType}/{Expression}): {Error}",
                specifications.Name,
                specifications.Type,
                specifications.Expression,
                data.Error?.Message
            );
            return data.Error ?? Fail("Mii studio serialization failed.");
        }

        var miiConfigKey = data.Value + specifications;
        if (!ShouldCache(specifications))
            return await RenderWithoutCacheAsync(mii, data.Value, specifications);

        // Fast path: return from cache before looking up or creating in-flight render work.
        if (_imageCache.TryGetValue(miiConfigKey, out Bitmap? cachedValue))
        {
            if (cachedValue != null)
                return cachedValue;
            return Fail("Cached image is null.");
        }

        var renderTask = _inFlightRequests.GetOrAdd(miiConfigKey, _ => RenderAndCacheAsync(mii, data.Value, specifications, miiConfigKey));
        try
        {
            return await renderTask;
        }
        finally
        {
            _inFlightRequests.TryRemove(new KeyValuePair<string, Task<OperationResult<Bitmap>>>(miiConfigKey, renderTask));
        }
    }

    private static bool ShouldCache(MiiImageSpecifications specifications) =>
        specifications.ExpirationSeconds.HasValue && specifications.ExpirationSeconds.Value > TimeSpan.Zero;

    private async Task<OperationResult<Bitmap>> RenderWithoutCacheAsync(Mii mii, string studioData, MiiImageSpecifications specifications)
    {
        var newImageResult = await nativeRenderer.RenderAsync(mii, studioData, specifications);
        if (newImageResult.IsFailure)
        {
            logger.LogWarning(
                "Native Mii render failed for uncached image '{ImageName}' ({BodyType}/{Expression}, size={Size}): {Error}",
                specifications.Name,
                specifications.Type,
                specifications.Expression,
                specifications.Size,
                newImageResult.Error?.Message
            );
            return newImageResult.Error!;
        }

        return newImageResult.Value;
    }

    private async Task<OperationResult<Bitmap>> RenderAndCacheAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        string cacheKey
    )
    {
        if (_imageCache.TryGetValue(cacheKey, out Bitmap? cached))
        {
            if (cached != null)
                return cached;
            return Fail("Cached image is null.");
        }

        var newImageResult = await nativeRenderer.RenderAsync(mii, studioData, specifications);
        if (newImageResult.IsFailure)
        {
            logger.LogWarning(
                "Native Mii render failed for image '{ImageName}' ({BodyType}/{Expression}, size={Size}): {Error}",
                specifications.Name,
                specifications.Type,
                specifications.Expression,
                specifications.Size,
                newImageResult.Error?.Message
            );
            return newImageResult.Error!;
        }

        var newImage = newImageResult.Value;
        using (var entry = _imageCache.CreateEntry(cacheKey))
        {
            entry.Value = newImage;
            entry.SlidingExpiration = specifications.ExpirationSeconds;
            entry.Priority = specifications.CachePriority;
            entry.Size = EstimateBitmapSizeBytes(newImage);
        }

        return newImage;
    }

    private static long EstimateBitmapSizeBytes(Bitmap image)
    {
        var width = image.PixelSize.Width;
        var height = image.PixelSize.Height;
        if (width <= 0 || height <= 0)
            return 1;

        return Math.Max(1L, checked((long)width * height * 4L));
    }

    public void Dispose()
    {
        _imageCache.Dispose();
    }
}
