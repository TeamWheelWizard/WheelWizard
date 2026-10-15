using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiImages.Views;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell.Couch;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement.Views;

public class MiiBlock : ToggleRadioButton
{
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
        set => SetValue(IsGlobalProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MiiProperty)
        {
            var mii = change.GetNewValue<Mii?>();
            MiiName = mii?.Name.ToString();
            IsFavorite = mii?.IsFavorite ?? false;
        }
        if (change.Property == CouchMode.IsEnabledProperty)
        {
            if (CouchMode.GetIsEnabled(this))
                Focusable = true;
            else
                ClearValue(FocusableProperty);
        }

        Tag = MiiName ?? String.Empty;
        Avalonia.Automation.AutomationProperties.SetName(this, MiiName ?? "Create Mii");
        ClipToBounds = string.IsNullOrWhiteSpace(MiiName);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (CouchMode.GetIsEnabled(this) && e.Key is Key.Enter or Key.Space)
        {
            IsChecked = !IsChecked;
            RaiseEvent(new RoutedEventArgs(ClickEvent));
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (CouchMode.GetIsEnabled(this) && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
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
