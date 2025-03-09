namespace WheelWizard.Models.MiiImages;

public class Mii 
{
    public required string Name { get; set; }
    public required string Data { get; set; }

    
    private Dictionary<MiiImageVariants.Variant, MiiImage> images = new ();

    public MiiImage GetImage(MiiImageVariants.Variant variant)
    {
        if (!images.ContainsKey(variant))
            images[variant] = new MiiImage(this, variant);
        return images[variant];
    }
}
