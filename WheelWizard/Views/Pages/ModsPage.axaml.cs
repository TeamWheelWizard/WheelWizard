using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WheelWizard.Models.Settings;
using WheelWizard.Services;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.ModManagement;

namespace WheelWizard.Views.Pages;

public record ModListItem(Mod Mod, bool IsLowest, bool IsHighest);

public partial class ModsPage : UserControlBase, INotifyPropertyChanged
{
    public ModManager ModManager => ModManager.Instance;

    public ObservableCollection<ModListItem> Mods =>
        new(
            ModManager.Mods.Select(mod => new ModListItem(
                mod,
                mod.Priority == ModManager.Instance.GetLowestActivePriority(),
                mod.Priority == ModManager.Instance.GetHighestActivePriority()
            ))
        );

    private bool _hasMods;

    public bool HasMods
    {
        get => _hasMods;
        set
        {
            if (_hasMods == value)
                return;

            _hasMods = value;
            OnPropertyChanged(nameof(HasMods));
        }
    }

    public ModsPage()
    {
        InitializeComponent();
        DataContext = this;
        ModManager.PropertyChanged += OnModsChanged;
        ModManager.ReloadAsync();
        SetModsViewVariant();
    }

    private void OnModsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModManager.Mods))
            OnModsChanged();
    }

    private void OnModsChanged()
    {
        ListItemCount.Text = ModManager.Mods.Count.ToString();
        OnPropertyChanged(nameof(Mods));
        HasMods = Mods.Count > 0;
        EnableAllCheckbox.IsChecked = !ModManager.Mods.Select(mod => mod.IsEnabled).Contains(false);
    }

    private void BrowseMod_Click(object sender, RoutedEventArgs e)
    {
        var modPopup = new ModBrowserWindow();
        modPopup.Show();
    }

    private void ImportMod_Click(object sender, RoutedEventArgs e)
    {
        ModManager.ImportMods();
    }

    private void RenameMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not ModListItem selectedMod)
            return;

        ModManager.RenameMod(selectedMod.Mod);
    }

    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not ModListItem selectedMod)
            return;

        ModManager.DeleteMod(selectedMod.Mod);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not ModListItem selectedMod)
            return;

        ModManager.OpenModFolder(selectedMod.Mod);
    }

    private void ViewMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not ModListItem selectedMod)
        {
            // You actually never see this error, however, if for some unknown reason it happens, we don't want to disregard it
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CantViewMod_SomethingWrong);
            return;
        }

        if (selectedMod.Mod.ModID == -1)
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CantViewMod_NotFromBrowser);
            return;
        }

        var modPopup = new ModIndependentWindow();
        _ = modPopup.LoadModAsync(selectedMod.Mod.ModID);
        modPopup.ShowDialog();
    }

    private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        ModManager.ToggleAllMods(EnableAllCheckbox.IsChecked == true);
    }

    private void PriorityText_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null || e.Source is not TextBox textBox)
            return;

        textBox.Classes.Remove("error"); // In case this class has been added, then we remove it again
        if (int.TryParse(textBox.Text, out var newPriority))
            mod.Priority = newPriority;
        else
            textBox.Text = mod.Priority.ToString();
    }

    private void PriorityText_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null || e.Source is not TextBox textBox)
            return;

        // We intentionally don't use the FeedbackTextBox here since that component is a bit to big for this use case.
        if (int.TryParse(textBox.Text, out _))
            textBox.Classes.Remove("error");
        else if (!textBox.Classes.Contains("error"))
            textBox.Classes.Add("error");
    }

    private Mod? GetParentsMod(RoutedEventArgs eventArgs)
    {
        var parent = ViewUtils.FindParent<ListBoxItem>(eventArgs.Source);
        if (parent?.Content is ModListItem mod)
            return mod.Mod;
        return null;
    }

    private void ButtonUp_OnClick(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null)
            return;

        ModManager.DecreasePriority(mod);
    }

    private void ButtonDown_OnClick(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null)
            return;

        ModManager.IncreasePriority(mod);
    }

    private void ToggleModsPageView_OnClick(object? sender, RoutedEventArgs e)
    {
        var current = (bool)SettingsManager.PREFERS_MODS_ROW_VIEW.Get();
        SettingsManager.PREFERS_MODS_ROW_VIEW.Set(!current);
        SetModsViewVariant();
    }

    private void SetModsViewVariant()
    {
        Control[] elementsToSwapClasses = [ToggleButton, ModsListBox];
        var asRows = (bool)SettingsManager.PREFERS_MODS_ROW_VIEW.Get();

        foreach (var elementToSwapClass in elementsToSwapClasses)
        {
            if (asRows)
                elementToSwapClass.Classes.Remove("Blocks");
            else
                elementToSwapClass.Classes.Add("Blocks");

            if (asRows)
                elementToSwapClass.Classes.Add("Rows");
            else
                elementToSwapClass.Classes.Remove("Rows");
        }
    }

    private void PriorityText_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox)
            return;
        ViewUtils.FindParent<ListBoxItem>(e.Source)?.Focus();
    }

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
