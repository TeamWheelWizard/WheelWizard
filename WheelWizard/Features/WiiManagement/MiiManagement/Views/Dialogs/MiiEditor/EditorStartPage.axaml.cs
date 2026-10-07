using Avalonia.Interactivity;
using Testably.Abstractions;
using WheelWizard.MiiAnimations.Editor;
using WheelWizard.Views.Components;
using MiiFactory = WheelWizard.WiiManagement.MiiManagement.MiiFactory;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Dialogs.MiiEditor;

public partial class EditorStartPage : MiiEditorBaseControl
{
    private IRandomSystem Random { get; }

    public EditorStartPage(IRandomSystem random, MiiEditorWindow ew)
        : base(ew)
    {
        Random = random;
        InitializeComponent();
        MiiName.Text = Editor.Mii.Name.ToString();
        if (Editor.Mii.IsFavorite)
            FavoriteButton.Classes.Add("favorite");
    }

    private void PopupPageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListActionButton button)
            return;

        if (button.CommandParameter is not Type pageType)
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
        // The new Mii appears in the middle of the shuffle, at the randomize marker.
        Editor.RefreshImage(MiiEditorReaction.Randomize, MiiEditorCues.Randomize);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Editor.Close();

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e) => Editor.SignalSaveMii();

    private void FavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.Mii.IsFavorite = !Editor.Mii.IsFavorite;

        FavoriteButton.Classes.Clear();
        if (Editor.Mii.IsFavorite)
            FavoriteButton.Classes.Add("favorite");
        Editor.React(Editor.Mii.IsFavorite ? MiiEditorReaction.Favorite : MiiEditorReaction.Unfavorite);
    }
}
