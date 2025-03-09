using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;

namespace WheelWizard.Views.Components.WhWzLibrary;

public class ModBrowserListItem : TemplatedControl
{
    public static readonly StyledProperty<string> ModTitleProperty =
        AvaloniaProperty.Register<ModBrowserListItem, string>(nameof(ModTitle));

    public string ModTitle
    {
        get => GetValue(ModTitleProperty);
        set => SetValue(ModTitleProperty, value);
    }
    
    public static readonly StyledProperty<string> ModAuthorProperty =
        AvaloniaProperty.Register<ModBrowserListItem, string>(nameof(ModAuthor));

    public string ModAuthor
    {
        get => GetValue(ModAuthorProperty);
        set => SetValue(ModAuthorProperty, value);
    }
    
    public static readonly StyledProperty<string> DownloadCountProperty =
        AvaloniaProperty.Register<ModBrowserListItem, string>(nameof(DownloadCount));

    public string DownloadCount
    {
        get => GetValue(DownloadCountProperty);
        set => SetValue(DownloadCountProperty, value);
    }
    
    public static readonly StyledProperty<string> ViewCountProperty =
        AvaloniaProperty.Register<ModBrowserListItem, string>(nameof(ViewCount));

    public string ViewCount
    {
        get => GetValue(ViewCountProperty);
        set => SetValue(ViewCountProperty, value);
    }
        
    public static readonly StyledProperty<string> LikeCountProperty =
        AvaloniaProperty.Register<ModBrowserListItem, string>(nameof(LikeCount));

    public string LikeCount
    {
        get => GetValue(LikeCountProperty);
        set => SetValue(LikeCountProperty, value);
    }
    
    public static readonly StyledProperty<string?> ImageUrlProperty =
        AvaloniaProperty.Register<ModBrowserListItem, string?>(nameof( ImageUrl));

    public string? ImageUrl
    {
        get => GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }
    
    protected override async void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var image = e.NameScope.Find<Image>("ThumbnailImage");
        if(image == null || ImageUrl == null) return;
        
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(ImageUrl);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        image.Source = new Bitmap(memoryStream);
    }
}

