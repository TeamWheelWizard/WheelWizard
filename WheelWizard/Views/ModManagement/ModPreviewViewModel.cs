using System.ComponentModel;
using Avalonia.Media.Imaging;
using SkiaSharp;
using WheelWizard.GameBanana;

namespace WheelWizard.Views.ModManagement;

public sealed class ModPreviewViewModel(
    int modId,
    IGameBananaSingletonService mods,
    IGameBananaMediaService media,
    string? previewUrl = null
) : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _loading;
    private bool _disposed;
    public Bitmap? Image { get; private set; }
    private Bitmap? _grayscaleImage;
    private byte[]? _imageBytes;
    public Bitmap? GrayscaleImage
    {
        get
        {
            if (Image is null || _grayscaleImage is not null)
                return _grayscaleImage;
            using var bitmap = SKBitmap.Decode(_imageBytes!);
            if (bitmap == null)
                return Image;
            using var gray = new SKBitmap(bitmap.Width, bitmap.Height);
            using var canvas = new SKCanvas(gray);
            using var filter = SKColorFilter.CreateColorMatrix(
                [0.2126f, 0.7152f, 0.0722f, 0, 0, 0.2126f, 0.7152f, 0.0722f, 0, 0, 0.2126f, 0.7152f, 0.0722f, 0, 0, 0, 0, 0, 1, 0]
            );
            using var paint = new SKPaint { ColorFilter = filter };
            canvas.DrawBitmap(bitmap, 0, 0, paint);
            using var encoded = gray.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = encoded.AsStream();
            return _grayscaleImage = new Bitmap(stream);
        }
    }
    public bool ShowPlaceholder => Image is null;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadAsync()
    {
        if (_disposed || (previewUrl is null && modId <= 0) || Image is not null)
            return Task.CompletedTask;
        // Share active requests, but let rebuilt cards retry a completed load that left no image.
        if (_loading is null || _loading.IsCompleted)
            _loading = LoadCoreAsync();
        return _loading;
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            var url = previewUrl;
            if (url is null)
            {
                var details = await mods.GetModDetails(modId).WaitAsync(_lifetime.Token);
                if (_disposed || details.IsFailure)
                    return;
                var preview = details.Value.PreviewMedia?.Images?.FirstOrDefault();
                if (preview is null)
                    return;
                url = $"{preview.BaseUrl}/{preview.File220 ?? preview.File}";
            }
            if (string.IsNullOrWhiteSpace(url))
                return;
            var result = await media.GetImageAsync(url, _lifetime.Token).WaitAsync(_lifetime.Token);
            if (_disposed || result.IsFailure)
                return;
            using var stream = new MemoryStream(result.Value);
            Image = new Bitmap(stream);
            _imageBytes = result.Value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch
        {
            // Preview failures leave the card's placeholder; mod installation remains available.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _grayscaleImage?.Dispose();
        _grayscaleImage = null;
        _imageBytes = null;
        Image?.Dispose();
        Image = null;
    }
}
