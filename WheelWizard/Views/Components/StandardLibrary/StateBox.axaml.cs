using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives; // Add this for PlacementMode
using Avalonia.Media;

namespace WheelWizard.Views.Components.StandardLibrary;

public partial class StateBox : TemplatedControl // Change to TemplatedControl
{
    public enum StateBoxVariantType
    {
        Default,
        Dark
    }
    
    public StateBox()
    {
        FontSize = 14;
        
        // TODO: make tooltip invisible if the TipText is empty
    }
    
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StateBox, string>(nameof(Text), "0");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<Geometry> IconDataProperty =
        AvaloniaProperty.Register<StateBox, Geometry>(nameof(IconData));

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<StateBox, double>(nameof(IconSize), 20);

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly StyledProperty<string> TipTextProperty =
        AvaloniaProperty.Register<StateBox, string>(nameof(TipText));

    public string TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public static readonly StyledProperty<StateBoxVariantType> VariantProperty =
        AvaloniaProperty.Register<StateBox, StateBoxVariantType>(nameof(Variant), StateBoxVariantType.Default);
    
    public StateBoxVariantType Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    
    public static readonly StyledProperty<PlacementMode> TipPlacementProperty =
        AvaloniaProperty.Register<StateBox, PlacementMode>(nameof(TipPlacement), PlacementMode.Right);

    public PlacementMode TipPlacement
    {
        get => GetValue(TipPlacementProperty);
        set => SetValue(TipPlacementProperty, value);
    }
    
    
    private void UpdateStyleClasses(StateBoxVariantType variant)
    {
        var types = Enum.GetValues<StateBoxVariantType>();
        foreach (var enumType in types)
        {
            Classes.Remove(enumType.ToString());
        }
        Classes.Add(variant.ToString());
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
            UpdateStyleClasses(change.GetNewValue<StateBoxVariantType>());
    }
}
