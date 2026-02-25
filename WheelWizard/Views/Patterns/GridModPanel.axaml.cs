using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using WheelWizard.GameBanana;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views.Patterns;

public partial class GridModPanel : UserControl
{
    private static readonly HttpClient s_httpClient = new();
    private static readonly ConcurrentDictionary<int, Bitmap> s_imageCache = new();
    private int? _currentModId;

    public GridModPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ModListItem item)
        {
            _currentModId = null;
            ModImage.Source = null;
            PlaceholderIcon.IsVisible = true;
            return;
        }

        var modId = item.Mod.ModID;
        _currentModId = modId;

        if (modId <= 0)
        {
            ModImage.Source = null;
            PlaceholderIcon.IsVisible = true;
            return;
        }

        if (s_imageCache.TryGetValue(modId, out var cachedImage))
        {
            ModImage.Source = cachedImage;
            PlaceholderIcon.IsVisible = false;
            return;
        }

        ModImage.Source = null;
        PlaceholderIcon.IsVisible = true;
        LoadModImageAsync(modId);
    }

    private async void LoadModImageAsync(int modId)
    {
        if (modId <= 0)
            return;

        try
        {
            var gameBananaService = App.Services.GetService<IGameBananaSingletonService>();
            if (gameBananaService == null)
                return;

            var result = await gameBananaService.GetModDetails(modId);
            if (!result.IsSuccess || result.Value.PreviewMedia?.Images == null || result.Value.PreviewMedia.Images.Count == 0)
                return;

            var image = result.Value.PreviewMedia.Images[0];
            // Prefer smaller 220px thumbnail for grid cards, fall back to full size
            var imageUrl = image.File220 != null ? $"{image.BaseUrl}/{image.File220}" : $"{image.BaseUrl}/{image.File}";

            var response = await s_httpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var bitmap = new Bitmap(memoryStream);
            if (!s_imageCache.TryAdd(modId, bitmap))
                bitmap.Dispose();

            if (_currentModId != modId)
                return;

            ModImage.Source = s_imageCache[modId];
            PlaceholderIcon.IsVisible = false;
        }
        catch
        {
            // Ignore - just show placeholder icon
        }
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
