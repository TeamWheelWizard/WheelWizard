using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.WiiManagement.MiiManagement.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorFacialHair : MiiEditorBaseControl
{
    private const int MinVertical = 0;
    private const int MaxVertical = 16; // Note: Range is 0-16
    private const int MinSize = 0;
    private const int MaxSize = 8; // Note: Range is 0-8

    public EditorFacialHair(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        PopulateValues();
    }

    private void PopulateValues()
    {
        // Attribute:
        var currentFacialHair = Editor.Mii.MiiFacialHair;

        // Colors both used for mustache and beard
        var color1 = new SolidColorBrush(ViewUtils.Colors.Neutral50); // Skin Color
        var color2 = new SolidColorBrush(ViewUtils.Colors.Neutral300); // Skin border Color
        var color3 = new SolidColorBrush(ViewUtils.Colors.Black); // Hair Color
        var color4 = new SolidColorBrush(ViewUtils.Colors.Danger400); // NONE Color
        var selectedColor4 = new SolidColorBrush(ViewUtils.Colors.Danger500);

        // Mustache:
        SetButtons(
            "MiiMustache",
            4,
            MustacheTypesGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)currentFacialHair.MiiMustacheType;
                button.Color1 = color1;
                button.Color2 = color2;
                button.Color3 = color3;
                button.Color4 = color4;
                button.SelectedColor4 = selectedColor4;
                button.Click += (_, _) => SetMustacheType(index);
            }
        );

        // Beards:  (also known as goatee internally)
        SetButtons(
            "MiiGoatee",
            4,
            BeardTypesGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)currentFacialHair.MiiBeardType;
                button.Color1 = color1;
                button.Color2 = color2;
                button.Color3 = color3;
                button.Color4 = color4;
                button.SelectedColor4 = selectedColor4;
                button.Click += (_, _) => SetBeardType(index);
            }
        );

        // Facial Hair Color:
        SetColorButtons(
            MiiColorMappings.HairColor.Count,
            HairColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)Editor.Mii.MiiFacialHair.Color;
                button.Color1 = new SolidColorBrush(MiiColorMappings.HairColor[(MiiHairColor)index]);
                button.Click += (_, _) => SetHairColor(index);
            }
        );

        // Transform attributes:
        MustacheTransformOptions.IsVisible = Editor.Mii.MiiFacialHair.MiiMustacheType != MiiMustacheType.None;
        UpdateTransformTextValues(currentFacialHair);
    }

    private void SetBeardType(int index)
    {
        if (Editor?.Mii?.MiiFacialHair == null || !IsLoaded)
            return;
        var current = Editor.Mii.MiiFacialHair;
        var beardType = (MiiBeardType)index;
        var result = MiiFacialHair.Create(current.MiiMustacheType, beardType, current.Color, current.Size, current.Vertical);
        if (result.IsFailure)
            return;

        Editor.Mii.MiiFacialHair = result.Value;
        Editor.RefreshImage();
    }

    private void SetHairColor(int index)
    {
        if (Editor?.Mii?.MiiFacialHair == null || !IsLoaded)
            return;
        var current = Editor.Mii.MiiFacialHair;
        var hairColor = (MiiHairColor)index;
        var result = MiiFacialHair.Create(current.MiiMustacheType, current.MiiBeardType, hairColor, current.Size, current.Vertical);
        if (result.IsFailure)
            return;

        Editor.Mii.MiiFacialHair = result.Value;
        Editor.RefreshImage();
    }

    private void SetMustacheType(int index)
    {
        if (Editor?.Mii?.MiiFacialHair == null || !IsLoaded)
            return;

        var current = Editor.Mii.MiiFacialHair;
        var mustacheType = (MiiMustacheType)index;
        var result = MiiFacialHair.Create(mustacheType, current.MiiBeardType, current.Color, current.Size, current.Vertical);
        if (result.IsFailure)
            return;

        MustacheTransformOptions.IsVisible = mustacheType != MiiMustacheType.None;
        Editor.Mii.MiiFacialHair = result.Value;
        Editor.RefreshImage();
    }

    #region Transform

    private void UpdateTransformTextValues(MiiFacialHair facialHair)
    {
        VerticalValueText.Text = ((facialHair.Vertical - 10) * -1).ToString();
        SizeValueText.Text = facialHair.Size.ToString();

        VerticalDecreaseButton.IsEnabled = facialHair.Vertical > MinVertical;
        VerticalIncreaseButton.IsEnabled = facialHair.Vertical < MaxVertical;
        SizeDecreaseButton.IsEnabled = facialHair.Size > MinSize;
        SizeIncreaseButton.IsEnabled = facialHair.Size < MaxSize;
    }

    // Consolidated helper method for button clicks
    private void TryUpdateFacialHairValue(int change, MiiTransformProperty property)
    {
        if (Editor?.Mii?.MiiFacialHair == null || !IsLoaded)
            return;

        var current = Editor.Mii.MiiFacialHair;
        int currentValue,
            newValue,
            min,
            max;

        // Determine current value, new value, and range based on property
        switch (property)
        {
            case MiiTransformProperty.Vertical:
                currentValue = current.Vertical;
                min = MinVertical;
                max = MaxVertical;
                break;
            case MiiTransformProperty.Size:
                currentValue = current.Size;
                min = MinSize;
                max = MaxSize;
                break;
            default:
                throw new ArgumentException($"{property} is not an option that you can change in FacialHair");
        }

        newValue = currentValue + change;
        if (newValue < min || newValue > max)
            return; // Value is out of range, do nothing

        var result = property switch
        {
            MiiTransformProperty.Vertical => MiiFacialHair.Create(
                current.MiiMustacheType,
                current.MiiBeardType,
                current.Color,
                current.Size,
                newValue
            ) // Note Vertical position
            ,
            MiiTransformProperty.Size => MiiFacialHair.Create(
                current.MiiMustacheType,
                current.MiiBeardType,
                current.Color,
                newValue,
                current.Vertical
            ) // Note Size position
            ,
            _ => throw new ArgumentException($"{property} is not an option that you can change in FacialHair"),
        };

        // Handle the result
        if (result.IsFailure)
            return;

        Editor.Mii.MiiFacialHair = result.Value;
        UpdateTransformTextValues(result.Value); // Update UI TextBlocks
        Editor.RefreshImage();
    }

    private void VerticalDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateFacialHairValue(-1, MiiTransformProperty.Vertical);

    private void VerticalIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateFacialHairValue(+1, MiiTransformProperty.Vertical);

    private void SizeDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateFacialHairValue(-1, MiiTransformProperty.Size);

    private void SizeIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateFacialHairValue(+1, MiiTransformProperty.Size);

    #endregion
}
