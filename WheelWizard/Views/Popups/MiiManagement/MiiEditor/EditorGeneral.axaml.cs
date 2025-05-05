using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorGeneral : MiiEditorBaseControl
{
    private bool _hasMiiNameError;
    private bool _hasCreatorNameError;
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
        MiiName.Text = Editor.Mii.Name.ToString();
        CreatorName.Text = Editor.Mii.CreatorName.ToString();
        GirlToggle.IsChecked = Editor.Mii.IsGirl;
        AllowCopy.IsChecked = Editor.Mii.CustomData.IsCopyable;
        LengthSlider.Value = Editor.Mii.Height.Value;
        WidthSlider.Value = Editor.Mii.Weight.Value;
        foreach (var color in Enum.GetNames(typeof(MiiFavoriteColor)))
        {
            FavoriteColorBox.Items.Add(color);
            if (color == Editor.Mii.MiiFavoriteColor.ToString())
                FavoriteColorBox.SelectedItem = color;
        }
    }

    protected override void BeforeBack()
    {
        // Rather than constantly making a new MiiName, we can also just set it when we return back to the start page
        if (!_hasMiiNameError)
            Editor.Mii.Name = new(MiiName.Text);
        if (!_hasCreatorNameError)
            Editor.Mii.CreatorName = new(CreatorName.Text);

        // For now i put it here, since i dont thing we want each value to be set when you change length or width
        // only when you stop moving that bar do we want that i think
        Editor.RefreshImage();
    }

    // We only have to check if it's a female, since if it's not, we already know the other option is going to be the male
    private void IsGirl_OnIsCheckedChanged(object? sender, RoutedEventArgs e) => Editor.Mii.IsGirl = GirlToggle.IsChecked == true;

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        // MiiName
        var validationMiiNameResult = ValidateMiiName(MiiName.Text);
        _hasMiiNameError = validationMiiNameResult.IsFailure;
        MiiName.ErrorMessage = validationMiiNameResult.Error?.Message ?? "";

        // CreatorName
        var validationCreatorNameResult = ValidateCreatorName(CreatorName.Text);
        _hasCreatorNameError = validationCreatorNameResult.IsFailure;
        CreatorName.ErrorMessage = validationCreatorNameResult.Error?.Message ?? "";
    }

    private OperationResult ValidateMiiName(string newName)
    {
        if (newName.Length is > 10 or < 3)
            return Fail("Name must be between 3 and 10 characters long.");

        return Ok();
    }

    private OperationResult ValidateCreatorName(string newName)
    {
        if (newName.Length > 10)
            return Fail("Creator name must be less than 10 characters long.");

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

    private void FavoriteColorBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var value = FavoriteColorBox.SelectedItem;
        if (value is null)
            return;
        var color = (MiiFavoriteColor)Enum.Parse(typeof(MiiFavoriteColor), value.ToString()!);
        if (color == Editor.Mii.MiiFavoriteColor)
            return;
        Editor.Mii.MiiFavoriteColor = color;
        Editor.RefreshImage();
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

    private void AllowCopy_OnIsCheckedChanged(object? sender, RoutedEventArgs e) =>
        Editor.Mii.CustomData.IsCopyable = AllowCopy.IsChecked == true;
}
