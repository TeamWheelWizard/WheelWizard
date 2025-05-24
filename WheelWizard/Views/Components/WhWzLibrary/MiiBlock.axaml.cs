using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WheelWizard.Services.Settings;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Components;

public class MiiBlock : RadioButton
{
    private static ContextMenu? s_oldMenu;

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<MiiBlock, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    public static readonly StyledProperty<string?> MiiNameProperty = AvaloniaProperty.Register<MiiBlock, string?>(nameof(MiiName));

    public string? MiiName
    {
        get => GetValue(MiiNameProperty);
        private set => SetValue(MiiNameProperty, value);
    }

    public static readonly StyledProperty<bool> IsFavoriteProperty = AvaloniaProperty.Register<MiiBlock, bool>(nameof(IsFavorite));

    public bool IsFavorite
    {
        get => GetValue(IsFavoriteProperty);
        private set => SetValue(IsFavoriteProperty, value);
    }

    public static readonly StyledProperty<bool> IsGlobalProperty = AvaloniaProperty.Register<MiiBlock, bool>(nameof(IsGlobal));

    public bool IsGlobal
    {
        get => GetValue(IsGlobalProperty);
        private set => SetValue(IsGlobalProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MiiProperty)
        {
            var mii = change.GetNewValue<Mii?>();
            MiiName = mii?.Name.ToString();
            IsFavorite = mii?.IsFavorite ?? false;
            IsGlobal = mii?.IsGlobal() ?? false;
        }

        Tag = MiiName ?? String.Empty;
        ClipToBounds = string.IsNullOrWhiteSpace(MiiName);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        e.Handled = true;

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
            IsChecked = !IsChecked;

        RaiseEvent(new RoutedEventArgs(ClickEvent));
    }
}
