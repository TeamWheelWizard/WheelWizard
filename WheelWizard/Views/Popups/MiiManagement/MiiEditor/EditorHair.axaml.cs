using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.WiiManagement.Domain;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorHair : MiiEditorBaseControl
{
    public EditorHair(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        PopulateValues();
    }

    private void PopulateValues()
    {
        // Attribute:
        var currentHair = Editor.Mii.MiiHair;

        // Hair:
        var color1 = new SolidColorBrush(ViewUtils.Colors.Neutral100); // Skin Color
        var color2 = new SolidColorBrush(ViewUtils.Colors.Neutral300); // Skin border Color
        var color3 = new SolidColorBrush(ViewUtils.Colors.Black); // Hair Color
        var color4 = new SolidColorBrush(ViewUtils.Colors.Primary800); // Hat main color
        var color5 = new SolidColorBrush(ViewUtils.Colors.Primary900); // Hat accent color
        SetButtons(
            "MiiHair",
            72,
            HairTypesGrid,
            (index, button) =>
            {
                button.IsChecked = index == currentHair.HairType;
                button.Color1 = color1;
                button.Color2 = color2;
                button.Color3 = color3;
                button.Color4 = color4;
                button.Color5 = color5;
                button.Click += (_, _) => SetHairType(index);
            }
        );

        // Hair color:
        SetColorButtons(
            MiiColorMappings.HairColor.Count,
            HairColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)Editor.Mii.MiiHair.MiiHairColor;
                button.Color1 = new SolidColorBrush(MiiColorMappings.HairColor[(MiiHairColor)index]);
                button.Click += (_, _) => SetHairColor(index);
            }
        );

        // Hair Flipped:
        HairFlippedCheck.IsChecked = currentHair.HairFlipped;
    }

    private void SetHairType(int type)
    {
        Editor.Mii.MiiHair = new(type, Editor.Mii.MiiHair.MiiHairColor, Editor.Mii.MiiHair.HairFlipped);
        Editor.RefreshImage();
    }

    private void SetHairColor(int color)
    {
        Editor.Mii.MiiHair = new(Editor.Mii.MiiHair.HairType, (MiiHairColor)color, Editor.Mii.MiiHair.HairFlipped);
        Editor.RefreshImage();
    }

    private void HairFlippedCheck_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (Editor?.Mii?.MiiHair == null || !IsLoaded)
            return;

        var isChecked = HairFlippedCheck.IsChecked == true;
        var currentHair = Editor.Mii.MiiHair;

        if (isChecked == currentHair.HairFlipped)
            return;

        var result = MiiHair.Create(currentHair.HairType, currentHair.MiiHairColor, isChecked); // New value

        if (result.IsFailure)
            return;

        Editor.Mii.MiiHair = result.Value;
        Editor.RefreshImage();
    }
}
