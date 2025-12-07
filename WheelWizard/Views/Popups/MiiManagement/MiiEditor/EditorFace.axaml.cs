using Avalonia.Controls;
using Avalonia.Media;
using WheelWizard.WiiManagement.MiiManagement.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorFace : MiiEditorBaseControl
{
    public EditorFace(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        PopulateValues();
    }

    private void PopulateValues()
    {
        // Attribute:
        var currentFacial = Editor.Mii.MiiFacialFeatures;

        // Facial features:
        foreach (var feature in Enum.GetNames(typeof(MiiFacialFeature)))
        {
            FacialFeatureBox.Items.Add(feature);
            if (feature == currentFacial.FacialFeature.ToString())
                FacialFeatureBox.SelectedItem = feature;
        }

        // Skin color:
        SetColorButtons(
            MiiColorMappings.SkinColor.Count,
            SkinColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)currentFacial.SkinColor;
                button.Color1 = new SolidColorBrush(MiiColorMappings.SkinColor[(MiiSkinColor)index]);
                button.Click += (_, _) => SetSkinColor(index);
            }
        );

        // Head shape:
        var headShapeColor1 = new SolidColorBrush(ViewUtils.Colors.Neutral50); // Skin Color
        var headShapeColor2 = new SolidColorBrush(ViewUtils.Colors.Neutral300); // Skin border Color
        var headShapeColor3 = new SolidColorBrush(Colors.Transparent); // Face features
        SetButtons(
            "MiiFace",
            8,
            HeadTypesGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)currentFacial.FaceShape;
                button.Color1 = headShapeColor1;
                button.Color2 = headShapeColor2;
                button.Color3 = headShapeColor3;
                button.Click += (_, _) => SetFaceType(index);
            }
        );
    }

    private void SetFaceType(int index)
    {
        var current = Editor.Mii.MiiFacialFeatures;
        //face shape is an enum so when comparing
        if (index == (int)current.FaceShape)
            return; // No change
        var result = MiiFacialFeatures.Create(
            (MiiFaceShape)index,
            current.SkinColor,
            current.FacialFeature,
            current.MingleOff,
            current.Downloaded
        );
        if (result.IsFailure)
            return;

        Editor.Mii.MiiFacialFeatures = result.Value;
        Editor.RefreshImage();
    }

    private void SetSkinColor(int index)
    {
        var current = Editor.Mii.MiiFacialFeatures;
        //face shape is an enum so when comparing
        if (index == (int)current.SkinColor)
            return; // No change
        var result = MiiFacialFeatures.Create(
            current.FaceShape,
            (MiiSkinColor)index,
            current.FacialFeature,
            current.MingleOff,
            current.Downloaded
        );
        if (result.IsFailure)
            return;

        Editor.Mii.MiiFacialFeatures = result.Value;
        Editor.RefreshImage();
    }

    private void FacialFeatureBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var value = FacialFeatureBox.SelectedItem;
        if (value is null)
            return;

        var selectedFeature = (MiiFacialFeature)Enum.Parse(typeof(MiiFacialFeature), value.ToString()!);
        var currentFacial = Editor.Mii.MiiFacialFeatures;

        if (selectedFeature == currentFacial.FacialFeature)
            return; // No change

        var result = MiiFacialFeatures.Create(
            currentFacial.FaceShape,
            currentFacial.SkinColor,
            selectedFeature,
            currentFacial.MingleOff,
            currentFacial.Downloaded
        );

        if (result.IsFailure)
        {
            ViewUtils.ShowSnackbar($"Error updating Facial Feature: {result.Error.Message}", ViewUtils.SnackbarType.Danger);
            return;
        }

        Editor.Mii.MiiFacialFeatures = result.Value;
        Editor.RefreshImage();
    }
}
