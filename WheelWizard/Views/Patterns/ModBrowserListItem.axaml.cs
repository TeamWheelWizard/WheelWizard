using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using WheelWizard.Views.ModManagement;

namespace WheelWizard.Views.Patterns;

public class ModBrowserListItem : TemplatedControl
{
    public static readonly StyledProperty<string> ModTitleProperty = AvaloniaProperty.Register<ModBrowserListItem, string>(
        nameof(ModTitle)
    );

    public string ModTitle
    {
        get => GetValue(ModTitleProperty);
        set => SetValue(ModTitleProperty, value);
    }

    public static readonly StyledProperty<string> ModAuthorProperty = AvaloniaProperty.Register<ModBrowserListItem, string>(
        nameof(ModAuthor)
    );

    public string ModAuthor
    {
        get => GetValue(ModAuthorProperty);
        set => SetValue(ModAuthorProperty, value);
    }

    public static readonly StyledProperty<string> DownloadCountProperty = AvaloniaProperty.Register<ModBrowserListItem, string>(
        nameof(DownloadCount)
    );

    public string DownloadCount
    {
        get => GetValue(DownloadCountProperty);
        set => SetValue(DownloadCountProperty, value);
    }

    public static readonly StyledProperty<string> ViewCountProperty = AvaloniaProperty.Register<ModBrowserListItem, string>(
        nameof(ViewCount)
    );

    public string ViewCount
    {
        get => GetValue(ViewCountProperty);
        set => SetValue(ViewCountProperty, value);
    }

    public static readonly StyledProperty<string> LikeCountProperty = AvaloniaProperty.Register<ModBrowserListItem, string>(
        nameof(LikeCount)
    );

    public string LikeCount
    {
        get => GetValue(LikeCountProperty);
        set => SetValue(LikeCountProperty, value);
    }

    public static readonly StyledProperty<ModPreviewViewModel?> PreviewProperty = AvaloniaProperty.Register<
        ModBrowserListItem,
        ModPreviewViewModel?
    >(nameof(Preview));

    public ModPreviewViewModel? Preview
    {
        get => GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public static readonly StyledProperty<bool> UsesPatchesProperty = AvaloniaProperty.Register<ModBrowserListItem, bool>(
        nameof(UsesPatches)
    );

    public bool UsesPatches
    {
        get => GetValue(UsesPatchesProperty);
        set => SetValue(UsesPatchesProperty, value);
    }

    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _ = Preview?.LoadAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && change.Property == PreviewProperty)
            _ = Preview?.LoadAsync();
    }
}
