using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using WheelWizard.Models.Mods;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views.Patterns;

public partial class GridModPanel : UserControl
{
    private Mod? _observedMod;

    public GridModPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        RootCardBorder.PointerEntered += (_, _) => UpdateThumbnail();
        RootCardBorder.PointerExited += (_, _) => UpdateThumbnail();
        CardSurface.SizeChanged += (_, _) => CardSurface.Clip = new RectangleGeometry(new Rect(CardSurface.Bounds.Size), 7, 7);
        RootCardBorder.PointerPressed += OnCardPointerPressed;
        RootCardBorder.PointerReleased += OnCardPointerReleased;
        Focusable = true;
        KeyDown += OnCardKeyDown;
    }

    private bool _cardPressed;

    private static bool IsPriorityInput(object? source) =>
        source is Visual visual && (visual is TextBox || visual.GetVisualAncestors().OfType<TextBox>().Any());

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _cardPressed = e.GetCurrentPoint(RootCardBorder).Properties.IsLeftButtonPressed && !IsPriorityInput(e.Source);
        if (_cardPressed)
            Focus();
    }

    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var toggle =
            _cardPressed && e.InitialPressMouseButton == MouseButton.Left && RootCardBorder.IsPointerOver && !IsPriorityInput(e.Source);
        _cardPressed = false;
        if (toggle && _observedMod != null)
        {
            _observedMod.IsEnabled = !_observedMod.IsEnabled;
            e.Handled = true;
        }
    }

    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !IsPriorityInput(e.Source) && _observedMod != null)
        {
            _observedMod.IsEnabled = !_observedMod.IsEnabled;
            e.Handled = true;
        }
    }

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
        if (_observedMod != null)
        {
            _observedMod.PropertyChanged -= OnModChanged;
            _observedMod.PropertyChanged += OnModChanged;
        }
        UpdateThumbnail();
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
