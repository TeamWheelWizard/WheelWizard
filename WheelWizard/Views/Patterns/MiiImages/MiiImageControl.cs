using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Patterns;

/// <summary>Public XAML input surface; the control theme owns rendering-view construction.</summary>
public abstract class MiiImageControl : TemplatedControl
{
    private BaseMiiImage? _renderer;

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<MiiImageControl, Mii?>(nameof(Mii));
    public static readonly StyledProperty<BaseMiiImage.ReloadMethodType> ReloadMethodProperty = AvaloniaProperty.Register<
        MiiImageControl,
        BaseMiiImage.ReloadMethodType
    >(nameof(ReloadMethod));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    public BaseMiiImage.ReloadMethodType ReloadMethod
    {
        get => GetValue(ReloadMethodProperty);
        set => SetValue(ReloadMethodProperty, value);
    }

    public event EventHandler? MiiImageLoaded;

    public void RefreshCurrentMii() => _renderer?.RefreshCurrentMii();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_renderer is not null)
            _renderer.MiiImageLoaded -= Renderer_OnImageLoaded;
        base.OnApplyTemplate(e);
        _renderer = e.NameScope.Find<BaseMiiImage>("PART_Renderer");
        if (_renderer is null)
            return;
        _renderer.MiiImageLoaded += Renderer_OnImageLoaded;
        if (_renderer.MiiLoaded)
            Renderer_OnImageLoaded(_renderer, EventArgs.Empty);
    }

    private void Renderer_OnImageLoaded(object? sender, EventArgs e) => MiiImageLoaded?.Invoke(this, e);
}
