using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.ComponentModel;
using WheelWizard.Models.Settings;
using WheelWizard.Services;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.ModManagement;
using ModPopupWindow = WheelWizard.Views.Popups.ModManagement.ModPopupWindow;

namespace WheelWizard.Views.Pages;

public partial class ModsPage : UserControl, INotifyPropertyChanged
{
    public ModManager ModManager => ModManager.Instance;
    public ObservableCollection<Mod> Mods => ModManager.Mods;
    
    private bool _hasMods;
    public bool HasMods
    {
        get => _hasMods;
        set
        {
            if (_hasMods == value) return;

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
        if(e.PropertyName == nameof(ModManager.Mods))
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
        var modPopup = new ModPopupWindow();
        modPopup.Show();
    }
    private void ImportMod_Click(object sender, RoutedEventArgs e)
    {
        ModManager.ImportMods();
    }

    private void RenameMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not Mod selectedMod)
             return;
        ModManager.RenameMod(selectedMod);
    }

    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not Mod selectedMod)
            return;
        
        ModManager.DeleteMod(selectedMod);
    }
    
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not Mod selectedMod)
            return;
        
        ModManager.OpenModFolder(selectedMod);
    }
    
    private void ViewMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsListBox.SelectedItem is not Mod selectedMod)
        {
            // You actually never see this error, however, if for some unknown reason it happens, we don't want to disregard it
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Cannot view the selected mod")
                .SetInfoText("Something went wrong when trying to open the selected mod")
                .Show();
            return;
        }
       
        if (selectedMod.ModID == -1)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Cannot view the selected mod")
                .SetInfoText("Cannot view mod that was not installed through the mod browser.")
                .Show();
            return;
        }
        
        var modPopup = new ModIndependentWindow();
        modPopup.LoadModAsync(selectedMod.ModID);
        modPopup.ShowDialog();
    }
    
        
    private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
       ModManager.ToggleAllMods(EnableAllCheckbox.IsChecked == true);
    }
    
        /*
    private static ListOrderCondition CurrentOrder = ListOrderCondition.PRIORITY;
    private void PopulateSortingList()
    {
        foreach (ListOrderCondition type in Enum.GetValues(typeof(ListOrderCondition)))
        {
            var name = type switch
            { // TODO: Should be replaced with actual translations
                ListOrderCondition.IS_CHECKED => "Is Enabled",
                ListOrderCondition.NAME => "Mod Name",
                ListOrderCondition.PRIORITY => "Priority"
            };

            SortByDropdown.Items.Add(name);
        }
        SortByDropdown.SelectedIndex = (int)CurrentOrder;
    }

    private void SortByDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CurrentOrder = (ListOrderCondition)SortByDropdown.SelectedIndex;
    }
    
    private enum ListOrderCondition
    {
        PRIORITY,
        IS_CHECKED,
        NAME
    }
    */
    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
    
    private void InputElement_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null || e.Source is not TextBox textBox) return;
        
        if(int.TryParse(textBox.Text, out var newPriority))
            mod.Priority = newPriority;
        else
            textBox.Text = mod.Priority.ToString();
    }
    
    private Mod? GetParentsMod(RoutedEventArgs eventArgs)
    {
        var parent = ViewUtils.FindParent<ListBoxItem>(eventArgs.Source);
        if(parent?.Content is Mod mod) return mod;
        return null;
    }

    private void ButtonUp_OnClick(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null) return;
        
        ModManager.DecreasePriority(mod);
    }
    
    private void ButtonDown_OnClick(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null) return;
        
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
            if(asRows) elementToSwapClass.Classes.Remove("Blocks");
            else elementToSwapClass.Classes.Add("Blocks");
            
            if(asRows) elementToSwapClass.Classes.Add("Rows");
            else elementToSwapClass.Classes.Remove("Rows");
        }
    }

    private void PriorityText_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox) return;
        ViewUtils.FindParent<ListBoxItem>(e.Source)?.Focus();
    }
}
