using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.WiiManagement.Domain;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorEyes : MiiEditorBaseControl
{
    private const int MinRotation = 0;
    private const int MaxRotation = 7;
    private const int MinVertical = 0;
    private const int MaxVertical = 18;
    private const int MinSize = 0;
    private const int MaxSize = 7;
    private const int MinSpacing = 0;
    private const int MaxSpacing = 12;

    public EditorEyes(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        PopulateValues();
    }

    private void PopulateValues()
    {
        // Attribute:
        var currentEyes = Editor.Mii.MiiEyes;

        // Eyes:
        var color1 = new SolidColorBrush(ViewUtils.Colors.Neutral50); // Eye white Color
        var color2 = new SolidColorBrush(ViewUtils.Colors.Neutral950); // Eye border Color
        var selectedColor2 = new SolidColorBrush(ViewUtils.Colors.Black);
        var color3 = new SolidColorBrush(ViewUtils.Colors.Primary400); // Eye Iris Color
        var selectedColor3 = new SolidColorBrush(ViewUtils.Colors.Primary300);
        SetButtons(
            "MiiEye",
            48,
            EyeTypesGrid,
            (index, button) =>
            {
                button.IsChecked = index == currentEyes.Type;
                button.Color1 = color1;
                button.Color2 = color2;
                button.SelectedColor2 = selectedColor2;
                button.Color3 = color3;
                button.Click += (_, _) => SetEyeType(index);
                button.SelectedColor3 = selectedColor3;
            }
        );

        // Eye Color:
        SetColorButtons(
            MiiColorMappings.EyeColor.Count,
            EyeColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)Editor.Mii.MiiEyes.Color;
                button.Color1 = new SolidColorBrush(MiiColorMappings.EyeColor[(MiiEyeColor)index]);
                button.Click += (_, _) => SetEyeColor(index);
            }
        );

        // Transform attributes:
        UpdateTransformTextValues(currentEyes);
    }

    private void SetEyeType(int index)
    {
        var current = Editor.Mii.MiiEyes;
        if (index == current.Type)
            return;

        var result = MiiEye.Create(index, current.Rotation, current.Vertical, current.Color, current.Size, current.Spacing);
        if (result.IsFailure)
            return;

        Editor.Mii.MiiEyes = result.Value;
        Editor.RefreshImage();
    }

    private void SetEyeColor(int index)
    {
        var current = Editor.Mii.MiiEyes;
        if (index == current.Type)
            return;

        var result = MiiEye.Create(current.Type, current.Rotation, current.Vertical, (MiiEyeColor)index, current.Size, current.Spacing);
        if (result.IsFailure)
            return;

        Editor.Mii.MiiEyes = result.Value;
        Editor.RefreshImage();
    }

    #region Transform

    private void UpdateTransformTextValues(MiiEye eyes)
    {
        VerticalValueText.Text = ((eyes.Vertical - 12) * -1).ToString();
        SizeValueText.Text = eyes.Size.ToString();
        RotationValueText.Text = (eyes.Rotation - 4).ToString();
        SpacingValueText.Text = eyes.Spacing.ToString();

        VerticalDecreaseButton.IsEnabled = eyes.Vertical > MinVertical;
        VerticalIncreaseButton.IsEnabled = eyes.Vertical < MaxVertical;
        SizeDecreaseButton.IsEnabled = eyes.Size > MinSize;
        SizeIncreaseButton.IsEnabled = eyes.Size < MaxSize;
        RotationDecreaseButton.IsEnabled = eyes.Rotation > MinRotation;
        RotationIncreaseButton.IsEnabled = eyes.Rotation < MaxRotation;
        SpacingDecreaseButton.IsEnabled = eyes.Spacing > MinSpacing;
        SpacingIncreaseButton.IsEnabled = eyes.Spacing < MaxSpacing;
    }

    private void TryUpdateEyeValue(int change, MiiTransformProperty property)
    {
        if (Editor?.Mii?.MiiEyes == null || !IsLoaded)
            return;

        var current = Editor.Mii.MiiEyes;
        int currentValue,
            newValue,
            min,
            max;
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
                throw new ArgumentException($"{property} is not an option that you can change in Eye");
        }

        newValue = currentValue + change;
        if (newValue < min || newValue > max)
            return;

        var result = property switch
        {
            MiiTransformProperty.Vertical => MiiEye.Create(
                current.Type,
                current.Rotation,
                newValue,
                current.Color,
                current.Size,
                current.Spacing
            ),
            MiiTransformProperty.Size => MiiEye.Create(
                current.Type,
                current.Rotation,
                current.Vertical,
                current.Color,
                newValue,
                current.Spacing
            ),
            MiiTransformProperty.Rotation => MiiEye.Create(
                current.Type,
                newValue,
                current.Vertical,
                current.Color,
                current.Size,
                current.Spacing
            ),
            MiiTransformProperty.Spacing => MiiEye.Create(
                current.Type,
                current.Rotation,
                current.Vertical,
                current.Color,
                current.Size,
                newValue
            ),
            _ => throw new ArgumentException($"{property} is not an option that you can change in Eye"),
        };

        if (result.IsFailure)
            return;

        Editor.Mii.MiiEyes = result.Value;
        UpdateTransformTextValues(result.Value);
        Editor.RefreshImage();
    }

    private void VerticalDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(-1, MiiTransformProperty.Vertical);

    private void VerticalIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(+1, MiiTransformProperty.Vertical);

    private void SizeDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(-1, MiiTransformProperty.Size);

    private void SizeIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(+1, MiiTransformProperty.Size);

    private void RotationDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(-1, MiiTransformProperty.Rotation);

    private void RotationIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(+1, MiiTransformProperty.Rotation);

    private void SpacingDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(-1, MiiTransformProperty.Spacing);

    private void SpacingIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateEyeValue(+1, MiiTransformProperty.Spacing);

    #endregion
}
