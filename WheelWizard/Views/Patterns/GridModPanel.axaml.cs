using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views.Patterns;

public partial class GridModPanel : UserControl
{
    public GridModPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ModListItem item)
            await item.Preview.LoadAsync();
    }

    private void PriorityText_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModListItem item || e.Source is not TextBox textBox)
            return;

        textBox.Classes.Remove("error");
        if (int.TryParse(textBox.Text, out var newPriority))
            item.Mod.Priority = newPriority;
        else
            textBox.Text = item.Mod.Priority.ToString();
    }

    private void PriorityText_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (e.Source is not TextBox textBox)
            return;

        if (int.TryParse(textBox.Text, out _))
            textBox.Classes.Remove("error");
        else if (!textBox.Classes.Contains("error"))
            textBox.Classes.Add("error");
    }

    private void PriorityText_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox)
            return;

        this.Focus();
    }
}
