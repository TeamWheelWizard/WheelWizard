using Avalonia.Media.Imaging;
using System.ComponentModel;
using WheelWizard.Services.WiiManagement;

namespace WheelWizard.Models.MiiImages;

public class MiiImage : INotifyPropertyChanged
{
    private Mii Parent { get; }
    public string Data => Parent.Data;
    public MiiImageVariants.Variant Variant { get; }
    public MiiImage(Mii parent, MiiImageVariants.Variant variant) => (Parent, Variant) = (parent,variant);

    public string CachingKey => $"{Data}_{Variant}";
    
    public bool LoadedImageSuccessfully { get; private set; } // default false, dont set this manually

    // This will never be set back to false, this is intentional
    // This is to ensure that it will never request the image again after the first time
    private bool _requestingImage;
    private Bitmap? _image;

    public Bitmap? Image
    {
        get
        {
            if (_image != null || _requestingImage) return _image;
            // it will set it to true, meaning this code can only be executed once due to the above check
            _requestingImage = true; 
        
            var newImage = MiiImageManager.GetCachedMiiImage(this);
            if (newImage == null)
                MiiImageManager.ResetMiiImageAsync(this);
            else
                SetImage(newImage.Value.Item1, newImage.Value.Item2);
        
            return _image;
        }
        private set
        {
            if (_image == value) return;
            _image = value;
            OnPropertyChanged(nameof(Image));
        }
    }

    public void SetImage(Bitmap image, bool loadedSuccessfully)
    {
        LoadedImageSuccessfully = loadedSuccessfully;
        Image = image;
    }
    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}
