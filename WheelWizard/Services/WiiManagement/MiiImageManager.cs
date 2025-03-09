using Avalonia.Media.Imaging;
using SkiaSharp;
using WheelWizard.Helpers;
using WheelWizard.Models.MiiImages;

namespace WheelWizard.Services.WiiManagement;

public static class MiiImageManager
{
    #region Mii Data

    private const int MaxCachedParsedMiiData = 126;
    private static readonly Dictionary<string, string?> ParsedMiiData = new();
    private static readonly Queue<string> ParsedMiiDataOrder = new();
    public static int ParsedMiiDataCount { get; private set; }

    private static void AddParsedMiiData(string rawData, string? parsedData)
    {
        if (!ParsedMiiData.ContainsKey(rawData))
            ParsedMiiDataOrder.Enqueue(rawData);
        ParsedMiiData[rawData] = parsedData;

        ParsedMiiDataCount = ParsedMiiData.Count;
        if (ParsedMiiData.Count <= MaxCachedParsedMiiData) return;
        var oldestMiiData = ParsedMiiDataOrder.Dequeue();
        ParsedMiiData.Remove(oldestMiiData);
        ParsedMiiDataCount = MaxCachedImages;
    }

    private static async Task<string?> LoadParsedMiiDataAsync(string? rawData)
    {
        if (string.IsNullOrEmpty(rawData))
            return null; // We don't even have to cache it, since null or empty is going to be this anyways

        if (ParsedMiiData.TryGetValue(rawData, out var value))
            return value;

        var newParsedData = await RequestParsedMiiDataAsync(rawData);
        AddParsedMiiData(rawData, newParsedData);
        return newParsedData;
    }

    private static async Task<string?> RequestParsedMiiDataAsync(string rawData)
    {
        using var formData = new MultipartFormDataContent();
        formData.Add(new ByteArrayContent(Convert.FromBase64String(rawData)), "data", "mii.dat");
        formData.Add(new StringContent("wii"), "platform");

        var response = await HttpClientHelper.PostAsync<ParsedMiiResponse>(Endpoints.MiiStudioUrl, formData);
        if (!response.Succeeded || response.Content?.Mii is null)
            return null;
        return response.Content.Mii;
    }

    private class ParsedMiiResponse
    {
        /// <summary>
        /// Mii Data
        /// </summary>
        public string? Mii { get; set; }

        public string? MiiStudio { get; set; }
        public string? Name { get; set; }
        public string? CreatorName { get; set; }
        public string? Birthday { get; set; }

        public string? FavoriteColor { get; set; }
        public int? Height { get; set; }
        public int? Build { get; set; }
        public string? Gender { get; set; }
        public string? Mingle { get; set; }
        public string? Copying { get; set; }
    }

    #endregion


    #region Mii Images

    private const int MaxCachedImages = 126;

    // There are always 2 values, the image itself, and a bool indicating if the image was loaded successfully
    // If it wasn't, it means that that image is just empty, yet it still should not be requested again since it failed
    private static readonly Dictionary<string, (Bitmap smallImage, bool success)> Images = new();
    private static readonly Queue<string> ImageOrder = new();
    public static int ImageCount { get; private set; } = 0;

    public static (Bitmap, bool)? GetCachedMiiImage(MiiImage miiConfig) => Images.TryGetValue(miiConfig.CachingKey, out var image) ? image : null;

    private static void AddMiiImage(MiiImage miiConfig, (Bitmap, bool) image)
    {
        if (!Images.ContainsKey(miiConfig.CachingKey))
            ImageOrder.Enqueue(miiConfig.CachingKey);
        Images[miiConfig.CachingKey] = image;

        ImageCount = Images.Count;
        if (Images.Count <= MaxCachedImages) return;
        var oldestMiiData = ImageOrder.Dequeue();
        Images.Remove(oldestMiiData);
        ImageCount = MaxCachedImages;
    }

    // Returns the image related to this data, if it is not cached, it will request it
    public static async Task<(Bitmap, bool)> LoadBase64MiiImageAsync(MiiImage miiConfig)
    {
        if (string.IsNullOrEmpty(miiConfig.Data))
            return (CreateEmptyBitmap(), false);

        if (Images.TryGetValue(miiConfig.CachingKey, out var value))
            return value;

        var newImage = await RequestMiiImageAsync(miiConfig);
        AddMiiImage(miiConfig, newImage);
        return newImage;
    }

    // Creates a new image of this Mii and adds it to the cache (if it was loaded successfully)
    public static async void ResetMiiImageAsync(MiiImage miiImage)
    {
        var newImage = await RequestMiiImageAsync(miiImage);
        AddMiiImage(miiImage, newImage);

        if (miiImage.Image == newImage.Item1) return;
        miiImage.SetImage(newImage.Item1, newImage.Item2);
    }

    // Return the image, and a bool indicating if the image was loaded successfully
    private static async Task<(Bitmap, bool)> RequestMiiImageAsync(MiiImage miiConfig)
    {
        var parsedMiiData = await LoadParsedMiiDataAsync(miiConfig.Data);
        if (string.IsNullOrEmpty(parsedMiiData)) return (CreateEmptyBitmap(), false);

        var miiImageUrl = MiiImageVariants.Get(miiConfig.Variant)(parsedMiiData);
        using var httpClient = new HttpClient();
        using var imageResponse = await httpClient.GetAsync($"{Endpoints.MiiImageUrl}?{miiImageUrl}");

        if (!imageResponse.IsSuccessStatusCode)
            return (CreateEmptyBitmap(), false);

        var imageStream = await imageResponse.Content.ReadAsStreamAsync();
        var bitmap = new Bitmap(imageStream);
        return (bitmap, true);
    }

    private static Bitmap CreateEmptyBitmap()
    {
        using var bitmap = new SKBitmap(1, 1);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent); // Or any color you prefer

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 1);
        return new(data.AsStream());
    }

    #endregion

    public static void ClearImageCache()
    {
        ParsedMiiData.Clear();
        ParsedMiiDataOrder.Clear();
        ParsedMiiDataCount = 0;

        Images.Clear();
        ImageOrder.Clear();
        ImageCount = 0;
    }
}
