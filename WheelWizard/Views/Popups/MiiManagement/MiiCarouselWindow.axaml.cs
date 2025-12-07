using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Base;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement;

public partial class MiiCarouselWindow : PopupContent
{
    public MiiCarouselWindow()
        : base(true, true, false, Common.PopupTitle_MiiCarousel)
    {
        InitializeComponent();
    }

    public MiiCarouselWindow SetMii(Mii newMii)
    {
        Window.WindowTitle = newMii.Name.ToString();
        Carousel.MiiImageLoaded += DisableLoadingIcon;
        Carousel.Mii = newMii;
        return this;
    }

    private void DisableLoadingIcon(object? sender, EventArgs e)
    {
        MiiLoadingIcon.IsVisible = false;
        Carousel.MiiImageLoaded -= DisableLoadingIcon;
    }
}
