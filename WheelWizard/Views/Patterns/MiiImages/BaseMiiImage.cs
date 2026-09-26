using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Patterns;

public abstract class BaseMiiImage : UserControlBase, INotifyPropertyChanged
{
    public enum ReloadMethodType
    {
        ClearAllThenInstanceNew, // Clears all images, then reloads each image when it is loaded
        ClearAllThenAllNew, // Clears all images, then reloads them when all images are all loaded again
        KeepAllUntilNew, // reloads all images, and only then swap them (aka, only then send signal out that they are changed)
        KeepInstanceUntilNew, // reload each image, and swap them if loaded. If there are more images, they will
    }

    private readonly IMiiImagesSingletonService MiiImageService;
    protected bool IsImageAttached { get; private set; }

    protected BaseMiiImage(IMiiImagesSingletonService images) => MiiImageService = images;

    public abstract void RefreshCurrentMii();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        IsImageAttached = true;
        RefreshCurrentMii();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        IsImageAttached = false;
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
        _reloadCts = null;
        base.OnDetachedFromVisualTree(e);
    }

    private bool _miiLoaded;
    public bool MiiLoaded
    {
        get => _miiLoaded;
        set
        {
            if (_miiLoaded == value)
                return;

            _miiLoaded = value;
            OnPropertyChanged(nameof(MiiLoaded));
            if (value)
                MiiImageLoaded?.Invoke(this, EventArgs.Empty);
        }
    }

    private ObservableCollection<Bitmap?> _generatedImages = new();
    public ObservableCollection<Bitmap?> GeneratedImages
    {
        get => _generatedImages;
        private set
        {
            if (_generatedImages == value)
                return;

            _generatedImages = value;
            OnPropertyChanged(nameof(GeneratedImages));
        }
    }

    public static readonly StyledProperty<ReloadMethodType> ReloadMethodProperty = AvaloniaProperty.Register<
        BaseMiiImage,
        ReloadMethodType
    >(nameof(ReloadMethod));

    public ReloadMethodType ReloadMethod
    {
        get => GetValue(ReloadMethodProperty);
        set => SetValue(ReloadMethodProperty, value);
    }

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<BaseMiiImage, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    static BaseMiiImage()
    {
        MiiProperty.Changed.AddClassHandler<BaseMiiImage>((view, change) => view.OnMiiChanged(change.GetNewValue<Mii?>()));
    }

    protected abstract void OnMiiChanged(Mii? newMii);

    private CancellationTokenSource? _reloadCts;

    protected async void ReloadImages(Mii? newMii, ICollection<MiiImageSpecifications> variants)
    {
        if (!IsImageAttached)
            return;

        // Cancel and dispose previous operation if any
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
        _reloadCts = new CancellationTokenSource();

        var cancellationToken = _reloadCts.Token;

        if (ReloadMethod is ReloadMethodType.ClearAllThenAllNew or ReloadMethodType.ClearAllThenInstanceNew)
        {
            GeneratedImages.Clear();
            OnPropertyChanged(nameof(GeneratedImages));
        }

        MiiLoaded = false;
        if (newMii == null)
        {
            MiiLoaded = true;
            return;
        }

        if (ReloadMethod is ReloadMethodType.KeepInstanceUntilNew or ReloadMethodType.ClearAllThenInstanceNew)
        {
            var index = 0;
            foreach (var variant in variants)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                // Assuming GetImageAsync accepts a CancellationToken
                var imageResult = await MiiImageService.GetImageAsync(newMii, variant);
                if (cancellationToken.IsCancellationRequested)
                    return;

                var imageToAdd = imageResult.IsSuccess ? imageResult.Value : null;
                if (index < GeneratedImages.Count)
                    GeneratedImages[index] = imageToAdd;
                else if (index == GeneratedImages.Count)
                    GeneratedImages.Add(imageToAdd);
                index++;
                OnPropertyChanged(nameof(GeneratedImages));
            }
        }
        else if (ReloadMethod is ReloadMethodType.KeepAllUntilNew or ReloadMethodType.ClearAllThenAllNew)
        {
            var loadedBitmaps = new List<Bitmap?>();
            foreach (var variant in variants)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                // Assuming GetImageAsync accepts a CancellationToken
                var imageResult = await MiiImageService.GetImageAsync(newMii, variant);
                if (cancellationToken.IsCancellationRequested)
                    return;
                loadedBitmaps.Add(imageResult.IsSuccess ? imageResult.Value : null);
            }

            GeneratedImages.Clear();
            foreach (var bmp in loadedBitmaps)
            {
                GeneratedImages.Add(bmp);
            }
            OnPropertyChanged(nameof(GeneratedImages));
        }

        MiiLoaded = true;
    }

    public event EventHandler? MiiImageLoaded;

    #region INotifyPropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
