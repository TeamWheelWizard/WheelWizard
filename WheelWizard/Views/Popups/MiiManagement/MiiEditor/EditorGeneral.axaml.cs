using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement.MiiManagement.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorGeneral : MiiEditorBaseControl
{
    private bool _hasMiiNameError;
    private bool _hasCreatorNameError;
    private bool _isPopulatingCustomDataControls;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(0.7), IsEnabled = false };

    public EditorGeneral(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        PopulateValues();
        _refreshTimer.Tick += RefreshTimer_Tick;
    }

    private void PopulateValues()
    {
        // random attributes:
        MiiName.Text = Editor.Mii.Name.ToString();
        CreatorName.Text = Editor.Mii.CreatorName.ToString();
        GirlToggle.IsChecked = Editor.Mii.IsGirl;
        AllowCopy.IsChecked = Editor.Mii.CustomDataV1.IsCopyable;
        LengthSlider.Value = Editor.Mii.Height.Value;
        WidthSlider.Value = Editor.Mii.Weight.Value;

        // Favorite color:
        SetColorButtons(
            MiiColorMappings.FavoriteColor.Count,
            FavoriteColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)Editor.Mii.MiiFavoriteColor;
                button.Color1 = new SolidColorBrush(MiiColorMappings.FavoriteColor[(MiiFavoriteColor)index]);
                button.Click += (_, _) => SetSkinColor(index);
            }
        );

        PopulateAccentColors();
        PopulateCustomDataDropDowns();
    }

    protected override void BeforeBack()
    {
        // Rather than constantly making a new MiiName, we can also just set it when we return back to the start page
        if (!_hasMiiNameError)
            Editor.Mii.Name = new(MiiName.Text);
        if (!_hasCreatorNameError)
            Editor.Mii.CreatorName = new(CreatorName.Text);

        // For nowI put it here, since I don't think we want each value to be set when you change length or width
        // only when you stop moving that bar so we want that, I think at least
        Editor.RefreshImage();
    }

    // We only have to check if it's a female, since if it's not, we already know the other option is going to be the male
    private void IsGirl_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        Editor.Mii.IsGirl = GirlToggle.IsChecked == true;
        Editor.RefreshImage();
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        // MiiName
        var validationMiiNameResult = ValidateMiiName(null, MiiName.Text);
        _hasMiiNameError = validationMiiNameResult.IsFailure;
        MiiName.ErrorMessage = validationMiiNameResult.Error?.Message ?? "";

        // CreatorName
        var validationCreatorNameResult = ValidateCreatorName(CreatorName.Text);
        _hasCreatorNameError = validationCreatorNameResult.IsFailure;
        CreatorName.ErrorMessage = validationCreatorNameResult.Error?.Message ?? "";
    }

    private OperationResult ValidateMiiName(string? _, string newName)
    {
        newName = newName?.Trim();
        if (newName.Length is > 10 or < 3)
            return Fail(Phrases.HelperNote_NameMustBetween);

        return Ok();
    }

    private OperationResult ValidateCreatorName(string newName)
    {
        newName = newName?.Trim();
        if (newName.Length > 10)
            return Fail(Phrases.HelperNote_CreatorNameLess11);

        return Ok();
    }

    private void Length_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var length = (int)LengthSlider.Value;
        if (length > 127)
            length = 127;
        if (length < 0)
            length = 0;
        var heightResult = MiiScale.Create((byte)length);
        if (heightResult.IsFailure)
        {
            ViewUtils.ShowSnackbar("Something went wrong while setting the height: " + heightResult.Error.Message);
            return;
        }
        Editor.Mii.Height = heightResult.Value;
        RestartRefreshTimer();
    }

    private void Width_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var width = (int)WidthSlider.Value;
        if (width > 127)
            width = 127;
        if (width < 0)
            width = 0;
        var weightResult = MiiScale.Create((byte)width);
        if (weightResult.IsFailure)
        {
            ViewUtils.ShowSnackbar("Something went wrong while setting the weight: " + weightResult.Error.Message);
            return;
        }
        Editor.Mii.Weight = weightResult.Value;
        RestartRefreshTimer();
    }

    private void SetSkinColor(int index)
    {
        Editor.Mii.MiiFavoriteColor = (MiiFavoriteColor)index;
        Editor.RefreshImage();
    }

    private void PopulateAccentColors()
    {
        SetColorButtons(
            Enum.GetValues<MiiProfileColor>().Length,
            AccentColorGrid,
            (index, button) =>
            {
                button.IsChecked = index == (int)Editor.Mii.CustomDataV1.AccentColor;
                var colorHex = MiiCustomMappings.GetAccentPreviewHex((MiiProfileColor)index);
                button.Color1 = new SolidColorBrush(Color.Parse($"#{colorHex}"));
                button.Click += (_, _) => SetAccentColor(index);
            }
        );
    }

    private void SetAccentColor(int index)
    {
        var selectedColor = (MiiProfileColor)index;
        if (Editor.Mii.CustomDataV1.AccentColor == selectedColor)
            return;

        Editor.Mii.CustomDataV1.AccentColor = selectedColor;
        Editor.RefreshImage();
    }

    private void PopulateCustomDataDropDowns()
    {
        _isPopulatingCustomDataControls = true;

        FacialExpressionBox.Items.Clear();
        foreach (var expression in Enum.GetValues<MiiPreferredFacialExpression>())
        {
            var item = new ComboBoxItem { Content = MiiCustomMappings.GetFacialExpressionLabel(expression), Tag = expression };
            FacialExpressionBox.Items.Add(item);
            if (expression == Editor.Mii.CustomDataV1.FacialExpression)
                FacialExpressionBox.SelectedItem = item;
        }

        CameraAngleBox.Items.Clear();
        foreach (var angle in Enum.GetValues<MiiPreferredCameraAngle>())
        {
            var item = new ComboBoxItem { Content = MiiCustomMappings.GetCameraAngleLabel(angle), Tag = angle };
            CameraAngleBox.Items.Add(item);
            if (angle == Editor.Mii.CustomDataV1.CameraAngle)
                CameraAngleBox.SelectedItem = item;
        }

        TaglineBox.Items.Clear();
        foreach (var tagline in Enum.GetValues<MiiPreferredTagline>())
        {
            var item = new ComboBoxItem { Content = MiiCustomMappings.GetTaglineLabel(tagline), Tag = tagline };
            TaglineBox.Items.Add(item);
            if (tagline == Editor.Mii.CustomDataV1.Tagline)
                TaglineBox.SelectedItem = item;
        }

        _isPopulatingCustomDataControls = false;
    }

    private void RestartRefreshTimer()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        Editor.RefreshImage();
    }

    private async void ComplexName_OnClick(object? sender, RoutedEventArgs e)
    {
        var textPopup = new TextInputWindow()
            .SetMainText(Phrases.Question_EnterNewName_Title)
            .SetExtraText(Humanizer.ReplaceDynamic(Phrases.Question_EnterNewName_Extra, MiiName.Text))
            .SetAllowCustomChars(true, true)
            .SetValidation(ValidateMiiName)
            .SetInitialText(MiiName.Text)
            .SetPlaceholderText(Phrases.Placeholder_EnterMiiName);
        var newName = await textPopup.ShowDialog();

        if (string.IsNullOrWhiteSpace(newName))
            return;
        MiiName.Text = newName;
    }

    private void AllowCopy_OnIsCheckedChanged(object? sender, RoutedEventArgs e) =>
        Editor.Mii.CustomDataV1.IsCopyable = AllowCopy.IsChecked == true;

    private void FacialExpressionBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingCustomDataControls)
            return;
        if (FacialExpressionBox.SelectedItem is not ComboBoxItem item || item.Tag is not MiiPreferredFacialExpression expression)
            return;
        if (Editor.Mii.CustomDataV1.FacialExpression == expression)
            return;

        Editor.Mii.CustomDataV1.FacialExpression = expression;
        Editor.RefreshImage();
    }

    private void CameraAngleBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingCustomDataControls)
            return;
        if (CameraAngleBox.SelectedItem is not ComboBoxItem item || item.Tag is not MiiPreferredCameraAngle angle)
            return;
        if (Editor.Mii.CustomDataV1.CameraAngle == angle)
            return;

        Editor.Mii.CustomDataV1.CameraAngle = angle;
        Editor.RefreshImage();
    }

    private void TaglineBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingCustomDataControls)
            return;
        if (TaglineBox.SelectedItem is not ComboBoxItem item || item.Tag is not MiiPreferredTagline tagline)
            return;
        if (Editor.Mii.CustomDataV1.Tagline == tagline)
            return;

        Editor.Mii.CustomDataV1.Tagline = tagline;
    }
}
