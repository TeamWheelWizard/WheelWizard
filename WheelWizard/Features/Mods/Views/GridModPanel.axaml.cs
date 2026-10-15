using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WheelWizard.Models.Mods;
using WheelWizard.Mods.ViewModels;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell.Couch;

namespace WheelWizard.Mods.Views;

public partial class GridModPanel : UserControl
{
    private Mod? _observedMod;

    public GridModPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        RootCardBorder.PointerEntered += (_, _) => UpdateThumbnail();
        RootCardBorder.PointerExited += (_, _) => UpdateThumbnail();
        Focusable = true;
    }

    public event EventHandler<RoutedEventArgs>? ViewRequested;
    public event EventHandler<PointerPressedEventArgs>? DragRequested;

    private void View_Click(object? sender, RoutedEventArgs e) => ViewRequested?.Invoke(this, e);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CouchMode.IsEnabledProperty)
            ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        Classes.Set("couch", CouchMode.GetIsEnabled(this));
        Focusable = !CouchMode.GetIsEnabled(this);
    }

    private void Drag_PointerPressed(object? sender, PointerPressedEventArgs e) => DragRequested?.Invoke(this, e);

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedMod != null)
            _observedMod.PropertyChanged -= OnModChanged;
        _observedMod = (DataContext as ModListItem)?.Mod;
        if (_observedMod != null)
            _observedMod.PropertyChanged += OnModChanged;
        UpdateThumbnail();
        if (DataContext is ModListItem item)
            await item.Preview.LoadAsync();
    }

    private void OnModChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Mod.IsEnabled))
            UpdateThumbnail();
    }

    private void UpdateThumbnail()
    {
        var gray = _observedMod is { IsEnabled: false } && !RootCardBorder.IsPointerOver;
        DisabledThumbnailImage.IsVisible = gray;
        ThumbnailImage.IsVisible = !gray;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_observedMod != null)
            _observedMod.PropertyChanged -= OnModChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyPresentation();
        if (_observedMod != null)
        {
            _observedMod.PropertyChanged -= OnModChanged;
            _observedMod.PropertyChanged += OnModChanged;
        }
        UpdateThumbnail();
    }

    private void PriorityText_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModListItem item || e.Source is not InputField textBox)
            return;

        textBox.ErrorText = null;
        if (int.TryParse(textBox.Text, out var newPriority))
            item.Mod.Priority = newPriority;
        else
            textBox.Text = item.Mod.Priority.ToString();
    }

    private void PriorityText_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (e.Source is not InputField textBox)
            return;

        textBox.ErrorText = int.TryParse(textBox.Text, out _)
            ? null
            : WheelWizard.Localization.TranslationFunctions.t("component.input.invalid_number");
    }

    private void PriorityText_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox)
            return;

        this.Focus();
    }
}
