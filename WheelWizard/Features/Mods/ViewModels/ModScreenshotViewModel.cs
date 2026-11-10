using System.ComponentModel;
using Avalonia.Media.Imaging;

namespace WheelWizard.Mods.ViewModels;

public sealed class ModScreenshotViewModel(string label) : INotifyPropertyChanged, IDisposable
{
    public string Label { get; } = label;
    public Bitmap? Image { get; private set; }
    public bool IsLoading { get; private set; } = true;
    public bool HasFailed => !IsLoading && Image is null;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Complete(Bitmap? image)
    {
        Image = image;
        IsLoading = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public void Dispose() => Image?.Dispose();
}
