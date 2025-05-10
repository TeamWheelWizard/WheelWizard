using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.WiiManagement.Domain;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorLips : MiiEditorBaseControl
{
    private const int MinVertical = 0;
    private const int MaxVertical = 18;
    private const int MinSize = 0;
    private const int MaxSize = 8;

    public EditorLips(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        PopulateValues();
    }

    private void PopulateValues()
    {
        // Attribute:
        var currentLips = Editor.Mii.MiiLips;

        // Lips:
        var color1 = new SolidColorBrush(new Color(255, 165, 57, 29)); // Lip Top Color
        var color2 = new SolidColorBrush(new Color(255, 255, 93, 13)); // Lip bottom Color
        var color3 = new SolidColorBrush(Colors.Black); // LipLine Color
        var color4 = new SolidColorBrush(Colors.White); // tooth color
        SetButtons(
            "MiiMouth",
            24,
            MouthTypesGrid,
            (index, button) =>
            {
                button.IsChecked = index == currentLips.Type;
                button.Color1 = color1;
                button.Color2 = color2;
                button.Color3 = color3;
                button.Color4 = color4;
                button.Click += (_, _) => SetMouthType(index);
            }
        );

        // Lip Colors:
        SetColorButtons(
            MiiColorMappings.LipBottomColor.Count,
            LipColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)Editor.Mii.MiiLips.Color;
                button.Color1 = new SolidColorBrush(MiiColorMappings.LipBottomColor[(MiiLipColor)index]);
                button.Click += (_, _) => SetMouthColor(index);
            }
        );

        // Transform attributes:
        UpdateTransformTextValues(currentLips);
    }

    private void SetMouthType(int index)
    {
        var current = Editor.Mii.MiiLips;
        if (index == current.Type)
            return;

        // MiiLip.Create(int type, LipColor color, int size, int vertical)
        var result = MiiLip.Create(index, current.Color, current.Size, current.Vertical);
        if (result.IsFailure)
            return;

        Editor.Mii.MiiLips = result.Value;
        Editor.RefreshImage();
    }

    private void SetMouthColor(int index)
    {
        var current = Editor.Mii.MiiLips;
        if (index == current.Type)
            return;

        // MiiLip.Create(int type, LipColor color, int size, int vertical)
        var result = MiiLip.Create(current.Type, (MiiLipColor)index, current.Size, current.Vertical);
        if (result.IsFailure)
            return;

        Editor.Mii.MiiLips = result.Value;
        Editor.RefreshImage();
    }

    private void UpdateTransformTextValues(MiiLip lips)
    {
        VerticalValueText.Text = ((lips.Vertical - 13) * -1).ToString();
        SizeValueText.Text = lips.Size.ToString();

        VerticalDecreaseButton.IsEnabled = lips.Vertical > MinVertical;
        VerticalIncreaseButton.IsEnabled = lips.Vertical < MaxVertical;
        SizeDecreaseButton.IsEnabled = lips.Size > MinSize;
        SizeIncreaseButton.IsEnabled = lips.Size < MaxSize;
    }

    #region Transform

    private void TryUpdateLipValue(int change, MiiTransformProperty property)
    {
        if (Editor?.Mii?.MiiLips == null || !IsLoaded)
            return;

        var current = Editor.Mii.MiiLips;
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
            default:
                throw new ArgumentException($"{property} is not an option that you can change in Lips");
        }

        newValue = currentValue + change;

        if (newValue < min || newValue > max)
            return;

        var result = property switch
        {
            MiiTransformProperty.Vertical => MiiLip.Create(current.Type, current.Color, current.Size, newValue),
            MiiTransformProperty.Size => MiiLip.Create(current.Type, current.Color, newValue, current.Vertical),
            _ => throw new ArgumentException($"{property} is not an option that you can change in Lips"),
        };

        if (result.IsFailure)
            return;

        Editor.Mii.MiiLips = result.Value;
        UpdateTransformTextValues(result.Value);
        Editor.RefreshImage();
    }

    private void VerticalDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateLipValue(-1, MiiTransformProperty.Vertical);

    private void VerticalIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateLipValue(+1, MiiTransformProperty.Vertical);

    private void SizeDecrease_Click(object? sender, RoutedEventArgs e) => TryUpdateLipValue(-1, MiiTransformProperty.Size);

    private void SizeIncrease_Click(object? sender, RoutedEventArgs e) => TryUpdateLipValue(+1, MiiTransformProperty.Size);

    #endregion
}
