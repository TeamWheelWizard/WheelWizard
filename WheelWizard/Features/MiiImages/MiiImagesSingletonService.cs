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

public class MiiImagesSingletonService(IMiiNativeRenderer nativeRenderer, IMemoryCache cache, ILogger<MiiImagesSingletonService> logger)
    : IMiiImagesSingletonService
{
    // Track in-flight requests to prevent duplicate renders.
    private readonly ConcurrentDictionary<string, Task<OperationResult<Bitmap>>> _inFlightRequests = new();

    public async Task<OperationResult<Bitmap>> GetImageAsync(Mii? mii, MiiImageSpecifications specifications)
    {
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
            return data.Error;
        }

        var miiConfigKey = data.Value + specifications;

        // Fast path: return from cache before looking up or creating in-flight render work.
        if (cache.TryGetValue(miiConfigKey, out Bitmap? cachedValue))
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

    private async Task<OperationResult<Bitmap>> RenderAndCacheAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        string cacheKey
    )
    {
        if (cache.TryGetValue(cacheKey, out Bitmap? cached))
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
        using (var entry = cache.CreateEntry(cacheKey))
        {
            entry.Value = newImage;
            entry.SlidingExpiration = specifications.ExpirationSeconds;
            entry.Priority = specifications.CachePriority;
        }

        return newImage;
    }
}
