using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WheelWizard.Views.Components;

public enum SegmentedControlVariant
{
    Dark,
    Light,
}

public class SegmentedControl : ListBox
{
    public static readonly StyledProperty<bool> HasBorderProperty = AvaloniaProperty.Register<SegmentedControl, bool>(
        nameof(HasBorder),
        true
    );
    public static readonly StyledProperty<Orientation> OrientationProperty = AvaloniaProperty.Register<SegmentedControl, Orientation>(
        nameof(Orientation)
    );
    public static readonly StyledProperty<SegmentedControlVariant> VariantProperty = AvaloniaProperty.Register<
        SegmentedControl,
        SegmentedControlVariant
    >(nameof(Variant));
    public bool HasBorder
    {
        get => GetValue(HasBorderProperty);
        set => SetValue(HasBorderProperty, value);
    }
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }
    public SegmentedControlVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
    private Border? _indicator;

    public SegmentedControl() =>
        LayoutUpdated += (_, _) =>
        {
            UpdateIndicator();
            var topLevel = TopLevel.GetTopLevel(this);
            var center = topLevel is null ? null : this.TranslatePoint(new Point(Bounds.Width / 2, 0), topLevel);
            var placement =
                Orientation == Orientation.Vertical
                    ? center?.X > topLevel?.Bounds.Width / 2
                        ? PlacementMode.Left
                        : PlacementMode.Right
                    : PlacementMode.Top;
            for (var index = 0; index < Items.Count; index++)
                if (ContainerFromIndex(index) is Control option)
                    ToolTip.SetPlacement(option, placement);
        };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (
            e.KeyModifiers == KeyModifiers.None
            && (
                e.Key is Key.Home or Key.End
                || (Orientation == Orientation.Horizontal ? e.Key is Key.Left or Key.Right : e.Key is Key.Up or Key.Down)
            )
        )
        {
            var direction = e.Key is Key.Left or Key.Up or Key.End ? -1 : 1;
            var index = e.Key switch
            {
                Key.Home => 0,
                Key.End => Items.Count - 1,
                _ => SelectedIndex < 0 ? (direction > 0 ? 0 : Items.Count - 1) : SelectedIndex + direction,
            };
            for (; index >= 0 && index < Items.Count; index += direction)
            {
                if (ContainerFromIndex(index) is not { IsEffectivelyEnabled: true, IsVisible: true } option)
                    continue;
                SetCurrentValue(SelectedIndexProperty, index);
                option.Focus();
                break;
            }
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty)
            UpdateIndicator();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _indicator = e.NameScope.Find<Border>("PART_SelectionIndicator");
        UpdateIndicator();
    }

    private void UpdateIndicator()
    {
        if (_indicator is null)
            return;
        var selected = ContainerFromIndex(SelectedIndex);
        var position = selected?.TranslatePoint(default, _indicator.GetVisualParent()!);
        _indicator.IsVisible = position.HasValue && selected!.Bounds.Width > 0;
        if (!_indicator.IsVisible)
            return;
        _indicator.Width = selected!.Bounds.Width;
        _indicator.Height = selected.Bounds.Height;
        var transform = (TranslateTransform)_indicator.RenderTransform!;
        transform.X = position!.Value.X;
        transform.Y = position.Value.Y;
    }
}

public class SegmentOption : ListBoxItem
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<SegmentOption, string?>(nameof(Text));
    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<SegmentOption, Geometry?>(
        nameof(IconData)
    );
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            SetCurrentValue(ContentProperty, Text);
    }
}
