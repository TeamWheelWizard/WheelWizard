namespace WheelWizard.Models.MiiImages;

public class Mii 
{
    public required string Name { get; set; }
    public required string Data { get; set; }

    
    private readonly Dictionary<MiiImageVariants.Variant, MiiImage> _images = new ();

    public MiiImage GetImage(MiiImageVariants.Variant variant)
    {
        if (!_images.ContainsKey(variant))
            _images[variant] = new(this, variant);
        return _images[variant];
    }
}
