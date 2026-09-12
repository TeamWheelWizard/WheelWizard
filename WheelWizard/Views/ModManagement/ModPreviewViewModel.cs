using System.ComponentModel;
using Avalonia.Media.Imaging;
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
    public bool ShowPlaceholder => Image is null;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadAsync()
    {
        if (_disposed || (previewUrl is null && modId <= 0) || Image is not null)
            return Task.CompletedTask;
        return _loading ??= LoadCoreAsync();
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
        Image?.Dispose();
        Image = null;
    }
}
