using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace WheelWizard.Views.Components.WhWzLibrary.MiiImages;

public class MiiImageLoader : BaseMiiImage
{
    private static bool _initializedRandomRotation = false;
    private static bool _randomRotation = false;
    
    public static readonly StyledProperty<IBrush> LoadingColorProperty =
        AvaloniaProperty.Register<MiiImageLoader, IBrush>(nameof(LoadingColor));

    public IBrush LoadingColor
    {
        get => GetValue(LoadingColorProperty);
        set => SetValue(LoadingColorProperty, value);
    }
    
    public static readonly StyledProperty<IBrush> FallBackColorProperty =
        AvaloniaProperty.Register<MiiImageLoader, IBrush>(nameof(FallBackColor));

    public IBrush FallBackColor
    {
        get => GetValue(FallBackColorProperty);
        set => SetValue(FallBackColorProperty, value);
    }
    
    public static readonly StyledProperty<bool> ConstraintRotationToVerticalProperty =
        AvaloniaProperty.Register<MiiImageLoader, bool>(nameof(ConstraintRotationToVertical));
    public bool ConstraintRotationToVertical
    {
        get => GetValue(ConstraintRotationToVerticalProperty);
        set => SetValue(ConstraintRotationToVerticalProperty, value);
    }
    
    private static readonly StyledProperty<bool> HeightAsScalingReferenceProperty =
        AvaloniaProperty.Register<MiiImageLoader, bool>(nameof(HeightAsScalingReference), true);
    public bool HeightAsScalingReference
    {
        get => GetValue(HeightAsScalingReferenceProperty);
        set => SetValue(HeightAsScalingReferenceProperty, value);
    }
    
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        
        var miiImageContainer = e.NameScope.Find<Grid>("MiiImageContainer");
        if (miiImageContainer == null) return;

        if (!_initializedRandomRotation)
        {
            _initializedRandomRotation = true;
           // check if today is april fools
           _randomRotation = (DateTime.Now.Month == 4) && (DateTime.Now.Day == 1);
        }
        if (_randomRotation)
        {
            // This will only happen as a joke, e.g. on april fools.
            miiImageContainer.RenderTransform = ConstraintRotationToVertical ? 
                new RotateTransform(180) : 
                new RotateTransform(new Random().NextDouble() * 360);
        }
            
            
        var loadingIcon = e.NameScope.Find<StandardLibrary.LoadingIcon>("MiiLoadingIcon");
        var fallbackIcon = e.NameScope.Find<PathIcon>("MiiFallBackIcon");
        
        Dispatcher.UIThread.Post(() =>
        {
            var newValue = HeightAsScalingReference ? 
                miiImageContainer.Bounds.Height :
                miiImageContainer.Bounds.Width;
            if (loadingIcon != null)
                loadingIcon.IconSize = newValue*0.7;

            if (fallbackIcon == null) return;
            fallbackIcon.Width = newValue * 0.85;
            fallbackIcon.Height = newValue * 0.85;
            fallbackIcon.Margin = new Thickness(0,0,0,-newValue * 0.05);

        }, DispatcherPriority.Render); 
    }
}
