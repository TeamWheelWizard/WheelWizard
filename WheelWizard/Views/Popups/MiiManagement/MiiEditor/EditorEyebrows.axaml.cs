using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.WiiManagement.MiiManagement.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorEyebrows : MiiEditorBaseControl
{
    // Define ranges for clarity and maintainability
    private const int MinVertical = 3;
    private const int MaxVertical = 18;
    private const int MinSize = 0;
    private const int MaxSize = 8;
    private const int MinRotation = 0;
    private const int MaxRotation = 11;
    private const int MinSpacing = 0;
    private const int MaxSpacing = 12;

    public EditorEyebrows(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        PopulateValues();
    }

    private void PopulateValues()
    {
        // Attribute:
        var currentEyebrows = Editor.Mii.MiiEyebrows;

        // Eyebrows:
        var color1 = new SolidColorBrush(ViewUtils.Colors.Danger400); // NONE Color
        var selectedColor1 = new SolidColorBrush(ViewUtils.Colors.Danger500);
        var color2 = new SolidColorBrush(ViewUtils.Colors.Black); // Eyebrow Color
        SetButtons(
            "MiiEyebrow",
            24,
            EyebrowTypesGrid,
            (index, button) =>
            {
                button.IsChecked = index == currentEyebrows.Type;
                button.Color1 = color1;
                button.SelectedColor1 = selectedColor1;
                button.Color2 = color2;
                button.Click += (_, _) => SetEyebrowType(index);
            }
        );

        // Eyebrow Color:
        SetColorButtons(
            MiiColorMappings.HairColor.Count,
            HairColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)Editor.Mii.MiiEyebrows.Color;
                button.Color1 = new SolidColorBrush(MiiColorMappings.HairColor[(MiiHairColor)index]);
                button.Click += (_, _) => SetEyebrowColor(index);
            }
        );

        // Transform attributes:
        UpdateTransformTextValues(currentEyebrows);
    }

    private void SetEyebrowType(int index)
    {
        if (Editor?.Mii?.MiiEyebrows == null)
            return;

        var current = Editor.Mii.MiiEyebrows;
        if (index == current.Type)
            return;

        var result = MiiEyebrow.Create(index, current.Rotation, current.Color, current.Size, current.Vertical, current.Spacing);
        if (result.IsFailure)
            return;

        Editor.Mii.MiiEyebrows = result.Value;
        Editor.RefreshImage();
    }

    private void SetEyebrowColor(int index)
    {
        if (Editor?.Mii?.MiiEyebrows == null)
            return;

        var current = Editor.Mii.MiiEyebrows;
        if (index == current.Type)
            return;

        var result = MiiEyebrow.Create(
            current.Type,
            current.Rotation,
            (MiiHairColor)index,
            current.Size,
            current.Vertical,
            current.Spacing
        );
        if (result.IsFailure)
            return;

        Editor.Mii.MiiEyebrows = result.Value;
        Editor.RefreshImage();
    }

    private void UpdateTransformTextValues(MiiEyebrow eyebrows)
    {
        SizeValueText.Text = eyebrows.Size.ToString();
        RotationValueText.Text = (eyebrows.Rotation - 6).ToString();
        SpacingValueText.Text = eyebrows.Spacing.ToString();
        VerticalValueText.Text = ((eyebrows.Vertical - 10) * -1).ToString();

        VerticalDecreaseButton.IsEnabled = eyebrows.Vertical > MinVertical;
        VerticalIncreaseButton.IsEnabled = eyebrows.Vertical < MaxVertical;
        SizeDecreaseButton.IsEnabled = eyebrows.Size > MinSize;
        SizeIncreaseButton.IsEnabled = eyebrows.Size < MaxSize;
        RotationDecreaseButton.IsEnabled = eyebrows.Rotation > MinRotation;
        RotationIncreaseButton.IsEnabled = eyebrows.Rotation < MaxRotation;
        SpacingDecreaseButton.IsEnabled = eyebrows.Spacing > MinSpacing;
        SpacingIncreaseButton.IsEnabled = eyebrows.Spacing < MaxSpacing;
    }

    #region Transform

    private void TryUpdateEyebrowValue(int change, MiiTransformProperty property)
    {
        if (Editor?.Mii?.MiiEyebrows == null || !IsLoaded)
            return;

        var current = Editor.Mii.MiiEyebrows;
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
            case MiiTransformProperty.Rotation:
                currentValue = current.Rotation;
                min = MinRotation;
                max = MaxRotation;
                break;
            case MiiTransformProperty.Spacing:
                currentValue = current.Spacing;
                min = MinSpacing;
                max = MaxSpacing;
                break;
            default:
                throw new ArgumentException($"{property} is not an option that you can change in Eyebrow");
        }

        newValue = currentValue + change;

        // Check range BEFORE attempting to create
        if (newValue < min || newValue > max)
            return;

        var result = property switch
        {
            MiiTransformProperty.Vertical => MiiEyebrow.Create(
                current.Type,
                current.Rotation,
                current.Color,
                current.Size,
                newValue,
                current.Spacing
            ),
            MiiTransformProperty.Size => MiiEyebrow.Create(
                current.Type,
                current.Rotation,
                current.Color,
                newValue,
                current.Vertical,
                current.Spacing
            ),
            MiiTransformProperty.Rotation => MiiEyebrow.Create(
                current.Type,
                newValue,
                current.Color,
                current.Size,
                current.Vertical,
                current.Spacing
            ),
            MiiTransformProperty.Spacing => MiiEyebrow.Create(
                current.Type,
                current.Rotation,
                current.Color,
                current.Size,
                current.Vertical,
                newValue
            ),
            _ => throw new ArgumentException($"{property} is not an option that you can change in Eyebrow"),
        };

        if (result.IsFailure)
            return;

        Editor.Mii.MiiEyebrows = result.Value;
        UpdateTransformTextValues(result.Value);
        Editor.RefreshImage();
    }

    private void VerticalDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(-1, MiiTransformProperty.Vertical);

    private void VerticalIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(+1, MiiTransformProperty.Vertical);

    private void SizeDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(-1, MiiTransformProperty.Size);

    private void SizeIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(+1, MiiTransformProperty.Size);

    private void RotationDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(-1, MiiTransformProperty.Rotation);

    private void RotationIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(+1, MiiTransformProperty.Rotation);

    private void SpacingDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(-1, MiiTransformProperty.Spacing);

    private void SpacingIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyebrowValue(+1, MiiTransformProperty.Spacing);

    #endregion
}
