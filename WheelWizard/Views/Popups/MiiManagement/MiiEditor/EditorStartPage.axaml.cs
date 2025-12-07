using Avalonia.Interactivity;
using Testably.Abstractions;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Components;
using MiiFactory = WheelWizard.WiiManagement.MiiManagement.MiiFactory;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public partial class EditorStartPage : MiiEditorBaseControl
{
    [Inject]
    private IRandomSystem Random { get; set; } = null!;

    public EditorStartPage(MiiEditorWindow ew)
        : base(ew)
    {
        InitializeComponent();
        MiiName.Text = Editor.Mii.Name.ToString();
        if (Editor.Mii.IsFavorite)
            FavoriteButton.Classes.Add("favorite");
    }

    private void PopupPageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not PopupListButton button)
            return;

        if (button.Type is not { } pageType)
            return;

        Editor.SetEditorPage(pageType);
    }

    private void RandomizeMii_OnClick(object? sender, RoutedEventArgs e)
    {
        var oldMii = Editor.Mii;
        var newMii = MiiFactory.CreateRandomMii(Random.Random.Shared);
        newMii.Name = oldMii.Name;
        newMii.IsFavorite = oldMii.IsFavorite;
        newMii.MiiId = oldMii.MiiId;
        newMii.SystemId = oldMii.SystemId;
        newMii.CreatorName = oldMii.CreatorName;

        Editor.SetMii(newMii);
        Editor.RefreshImage();
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Editor.Close();

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e) => Editor.SignalSaveMii();

    private void FavoriteButton_OnClick(object? sender, EventArgs e)
    {
        Editor.Mii.IsFavorite = !Editor.Mii.IsFavorite;

        FavoriteButton.Classes.Clear();
        if (Editor.Mii.IsFavorite)
            FavoriteButton.Classes.Add("favorite");
    }
}
