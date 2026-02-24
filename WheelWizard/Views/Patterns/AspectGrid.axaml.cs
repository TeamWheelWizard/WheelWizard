using Avalonia;
using Avalonia.Controls;

namespace WheelWizard.Views.Patterns;

public partial class AspectGrid : Grid
{
    #region Properties

    /// <summary>
    /// Defines the AspectRatio property.
    /// </summary>
    public static readonly StyledProperty<double> AspectRatioProperty = AvaloniaProperty.Register<AspectGrid, double>(
        nameof(AspectRatio),
        1.0
    );

    /// <summary>
    /// Gets or sets the aspect ratio. Default is 1.0 (square).
    /// </summary>
    public double AspectRatio
    {
        get => GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    /// <summary>
    /// Defines the UseMaxDimension property.
    /// </summary>
    public static readonly StyledProperty<bool> UseMaxDimensionProperty = AvaloniaProperty.Register<AspectGrid, bool>(
        nameof(UseMaxDimension)
    );

    /// <summary>
    /// Gets or sets whether to use the maximum dimension for sizing.
    /// If true, uses the larger of width/height. If false, uses the smaller.
    /// </summary>
    public bool UseMaxDimension
    {
        get => GetValue(UseMaxDimensionProperty);
        set => SetValue(UseMaxDimensionProperty, value);
    }

    #endregion

    public AspectGrid()
    {
        InitializeComponent();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        var heightSize = UseMaxDimension
            ? Math.Max(e.NewSize.Width / AspectRatio, e.NewSize.Height)
            : Math.Min(e.NewSize.Width / AspectRatio, e.NewSize.Height);

        // Set both width and height to the larger dimension to create a square
        Width = heightSize * AspectRatio;
        Height = heightSize;
    }
}
