using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using WheelWizard.Models.MiiImages;

namespace WheelWizard.Views.Components.WhWzLibrary.MiiImages;

public abstract class BaseMiiImage : TemplatedControl, INotifyPropertyChanged
{
    public static readonly StyledProperty<bool> MiiLoadedProperty =
        AvaloniaProperty.Register<BaseMiiImage, bool>(nameof(MiiLoaded));
    protected bool MiiLoaded
    {
        get => GetValue(MiiLoadedProperty);
        set
        {
            SetValue(MiiLoadedProperty, value);
            OnPropertyChanged(nameof(MiiLoaded));
            if(value)
                MiiImageLoaded?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public static readonly StyledProperty<Bitmap?> MiiImageProperty =
        AvaloniaProperty.Register<BaseMiiImage, Bitmap?>(nameof(MiiImage));
    protected Bitmap? MiiImage
    {
        get => GetValue(MiiImageProperty);
        set
        {
            SetValue(MiiImageProperty, value);
            OnPropertyChanged(nameof(MiiImage));
        }
    }
    
   public static readonly StyledProperty<MiiImageVariants.Variant> ImageVariantProperty =
         AvaloniaProperty.Register<BaseMiiImage, MiiImageVariants.Variant>(
             nameof(ImageVariant), MiiImageVariants.Variant.SMALL, coerce: CoerceVariant);
    
    public MiiImageVariants.Variant ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }
        
    public static readonly StyledProperty<Mii?> MiiProperty =
        AvaloniaProperty.Register<BaseMiiImage, Mii?>(nameof(Mii), coerce: CoerceMii);
    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }
    
    private static MiiImageVariants.Variant CoerceVariant(AvaloniaObject o, MiiImageVariants.Variant value)
    {
        ((BaseMiiImage)o).OnVariantChanged(value);
        return value;
    }
    private static Mii? CoerceMii(AvaloniaObject o, Mii? value)
    {
        ((BaseMiiImage)o).OnMiiChanged(value);
        return value;
    }

    protected void OnVariantChanged(MiiImageVariants.Variant newValue)
    {
        ReloadImage(Mii?.GetImage(ImageVariant), Mii?.GetImage(newValue));
    }

    protected void OnMiiChanged(Mii? newValue)
    {
        ReloadImage(Mii?.GetImage(ImageVariant), newValue?.GetImage(ImageVariant));
    } 

    protected void ReloadImage(MiiImage? oldImage, MiiImage? newImage)
    {
        if (oldImage != null) oldImage.PropertyChanged -= NotifyMiiImageChangedInternally;
       
        if (newImage != null) 
            newImage.PropertyChanged += NotifyMiiImageChangedInternally;
        MiiImage = newImage?.Image;
        MiiLoaded = newImage?.LoadedImageSuccessfully == true;
        
        MiiChanged?.Invoke(this, EventArgs.Empty);
    }
    protected void NotifyMiiImageChangedInternally(object? sender, PropertyChangedEventArgs args)
    {
        var variantedImage = Mii?.GetImage(ImageVariant);
        if (args.PropertyName != nameof(variantedImage.Image)) return;
        MiiImage = Mii?.GetImage(ImageVariant).Image;
        MiiLoaded = Mii?.GetImage(ImageVariant).LoadedImageSuccessfully == true;
    }

    public event EventHandler MiiChanged;
    public event EventHandler MiiImageLoaded;
    
    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}

