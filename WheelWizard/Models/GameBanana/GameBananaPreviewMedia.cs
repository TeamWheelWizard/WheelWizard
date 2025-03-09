// ReSharper disable InconsistentNaming

namespace WheelWizard.Models.GameBanana;

public class GameBananaPreviewMedia
{
    public List<Image> _aImages { get; set; } = [];
    public string? FirstImageUrl => _aImages.Count > 0 ? $"{_aImages[0]._sBaseUrl}/{_aImages[0]._sFile}" : null;
    public class Image
    {
        public string _sType { get; set; } // media type (e.g., "screenshot")
        public string _sBaseUrl { get; set; }
        public string _sFile { get; set; }
            
        public int _wFile100 { get; set; } // Width and height for a 100px version of the image
        public int _hFile100 { get; set; }
    }
}
