using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Caching.Memory;
using WheelWizard.MiiImages.Domain;
using WheelWizard.Shared.Services;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages;

public interface IMiiImagesSingletonService
{
    Task<OperationResult<Bitmap>> GetImageAsync(Mii? mii, MiiImageSpecifications specifications);
}

public class MiiImagesSingletonService(IApiCaller<IMiiIMagesApi> apiCaller, IMemoryCache cache) : IMiiImagesSingletonService
{
    // Track in-flight requests to prevent duplicate API calls
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _inFlightRequests = new();

    public async Task<OperationResult<Bitmap>> GetImageAsync(Mii? mii, MiiImageSpecifications specifications)
    {
        var data = MiiStudioDataSerializer.Serialize(mii);
        if (data.IsFailure)
            return data.Error;

        var miiConfigKey = data.Value + specifications;

        // Even tho we also check it in the semaphore section, we also check here if it's in the cache, just to be tad faster.
        if (cache.TryGetValue(miiConfigKey, out Bitmap? cachedValue))
        {
            if (cachedValue != null)
                return cachedValue;
            return Fail("Cached image is null.");
        }

        var requestSemaphore = _inFlightRequests.GetOrAdd(miiConfigKey, _ => new(1, 1));

        try
        {
            // Wait to acquire the semaphore - only the first request will proceed immediately
            await requestSemaphore.WaitAsync();

            // Double-check the cache after acquiring the semaphore
            // Another thread might have completed the request while we were waiting
            if (cache.TryGetValue(miiConfigKey, out Bitmap? doubleCheckCached))
            {
                if (doubleCheckCached != null)
                    return doubleCheckCached;
                return Fail("Cached image is null.");
            }

            // If we get here, we're the first request and need to call the API
            var newImageResult = await apiCaller.CallApiAsync(api => GetBitmapAsync(api, data.Value, specifications));

            Bitmap? newImage = null;
            if (newImageResult.IsSuccess)
                newImage = newImageResult.Value;

            using (var entry = cache.CreateEntry(miiConfigKey))
            {
                entry.Value = newImage;
                entry.SlidingExpiration = specifications.ExpirationSeconds;
                entry.Priority = specifications.CachePriority;
            }

            if (newImage != null)
                return newImage;
            return Fail("Failed to get new image.");
        }
        finally
        {
            // We can also do it all without try catch. But we need to make sure that whatever happens, we release the semaphore
            // So just to be safe, if anything happens, we release the semaphore anyway.
            requestSemaphore.Release();
            _inFlightRequests.TryRemove(miiConfigKey, out _);
        }
    }

    private static async Task<Bitmap> GetBitmapAsync(IMiiIMagesApi api, string data, MiiImageSpecifications specifications)
    {
        var result = await api.GetImageAsync(
            data,
            specifications.Type.ToString(),
            specifications.Expression.ToString(),
            (int)specifications.Size,
            characterXRotate: (int)specifications.CharacterRotate.X,
            characterYRotate: (int)specifications.CharacterRotate.Y,
            characterZRotate: (int)specifications.CharacterRotate.Z,
            bgColor: specifications.BackgroundColor,
            instanceCount: specifications.InstanceCount,
            cameraXRotate: (int)specifications.CameraRotate.X,
            cameraYRotate: (int)specifications.CameraRotate.Y,
            cameraZRotate: (int)specifications.CameraRotate.Z
        );

        using var memoryStream = new MemoryStream();
        await result.CopyToAsync(memoryStream);
        memoryStream.Position = 0; // Reset stream position for Bitmap constructor

        if (memoryStream.Length == 0)
            throw new InvalidOperationException("Received empty image stream.");

        var bitmap = new Bitmap(memoryStream);
        return bitmap;
    }
}
