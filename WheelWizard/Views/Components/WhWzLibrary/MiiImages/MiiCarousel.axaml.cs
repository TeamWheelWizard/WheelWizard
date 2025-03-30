using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using WheelWizard.Models.MiiImages;

namespace WheelWizard.Views.Components.MiiImages;

public class MiiCarousel : BaseMiiImage
{
    private const int CarouselInstanceCount = 8;
    private int _currentCarouselInstance;
    
    
    public MiiCarousel()
    {
        ImageVariant = MiiImageVariants.Variant.FULL_BODY_CAROUSEL;
    }
    
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    { 
        base.OnApplyTemplate(e);

        var miiImage = e.NameScope.Find<Image>("MiiImage");
        if (miiImage != null)
            miiImage.Width = Height*CarouselInstanceCount;
        
        var miiImageContainer = e.NameScope.Find<Border>("MiiImageCounter");
        if (miiImageContainer != null)
        {
            var transGroup =  new TransformGroup();
            transGroup.Children.Add(new ScaleTransform(1.5, 1.5));
            transGroup.Children.Add(new TranslateTransform(0, -Height*0.20));
            miiImageContainer.RenderTransform = transGroup;
        }
        
        void RotateLeft(object? obj, EventArgs _)
        {
            _currentCarouselInstance += 1;
            if(_currentCarouselInstance > 0) _currentCarouselInstance -= CarouselInstanceCount;
            _currentCarouselInstance %= CarouselInstanceCount;
            if(miiImage != null)
                miiImage.RenderTransform = new TranslateTransform(_currentCarouselInstance * Height, 0);
        }
        void RotateRight(object? obj, EventArgs _)
        {
            _currentCarouselInstance -= 1;
            _currentCarouselInstance %= CarouselInstanceCount;
            if(miiImage != null)
                miiImage.RenderTransform = new TranslateTransform(_currentCarouselInstance * Height, 0);
        }
        
        var rotateLeft = e.NameScope.Find<Button>("RotateLeftButton");
        if (rotateLeft != null)
            rotateLeft.Click += RotateLeft;
        
        var rotateRight = e.NameScope.Find<Button>("RotateRightButton");
        if (rotateRight != null)
            rotateRight.Click += RotateRight;
    }

}

