using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using WheelWizard.Helpers;
using WheelWizard.Models.GameBanana;
using WheelWizard.Services;
using WheelWizard.Services.GameBanana;
using WheelWizard.Services.Installation;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Popups.ModManagement;

public partial class ModDetailViewer : UserControl
{
    private bool loading;
    private bool loadingVisual;
    private GameBananaModDetails? CurrentMod { get; set; }
    
    public ModDetailViewer()
    {
        InitializeComponent();
        ResetVisibility();
        UnInstallButton.IsVisible = false;
    }

    private void ResetVisibility()
    {
        // Method returns false if the details page is not shown
        if (loadingVisual)
        {
            LoadingView.IsVisible = true;
            NoDetailsView.IsVisible = false;
            DetailsView.IsVisible = false;
            return;
        }
        if (CurrentMod == null)
        {
            LoadingView.IsVisible = false;
            NoDetailsView.IsVisible = true;
            DetailsView.IsVisible = false;
            return;
        }

        LoadingView.IsVisible = false;
        NoDetailsView.IsVisible = false;
        DetailsView.IsVisible = true;
    }

    /// <summary>
    /// Loads the details of the specified mod into the viewer.
    /// </summary>
    /// <param name="ModId">The ID of the mod to load.</param>
    /// <param name="newDownloadUrl">The download URL to use instead of the one from the mod details.</param>
    public async Task<bool> LoadModDetailsAsync(int ModId, string? newDownloadUrl = null, CancellationToken cancellationToken = default)
    {
        // Check if cancellation has been requested before starting
        if (cancellationToken.IsCancellationRequested) return false;
        // Set the UI to show loading state
        loadingVisual = true;
        loading = true;
        ResetVisibility();
    
        // Retrieve the mod details.
        // If GamebananaSearchHandler.GetModDetailsAsync supports cancellation,
        // consider passing the token as a parameter.
        var modDetailsResult = await GamebananaSearchHandler.GetModDetailsAsync(ModId);
        if (cancellationToken.IsCancellationRequested) return false;
    
        if (!modDetailsResult.Succeeded || modDetailsResult.Content == null)
        {
            CurrentMod = null;
            NoDetailsView.Title = "Failed to retrieve mod info";
            NoDetailsView.BodyText = modDetailsResult.StatusMessage ?? "An error occurred while fetching mod details.";
            
            loading = false;
            loadingVisual = false;
            ResetVisibility();
            return false;
        }
    
        CurrentMod = modDetailsResult.Content;
    
        // Update the UI with mod details
        ModTitle.Text = CurrentMod._sName;
        AuthorButton.Text = CurrentMod._aSubmitter._sName;
        LikesCountBox.Text = CurrentMod._nLikeCount.ToString();
        ViewsCountBox.Text = CurrentMod._nViewCount.ToString();
        DownloadsCountBox.Text = CurrentMod._nDownloadCount.ToString();
    
        // Wrap the mod description in a div tag so that CSS can be applied
        ModDescriptionHtmlPanel.Text = $"<body>{CurrentMod._sText}</body>";
        CurrentMod.OverrideDownloadUrl = newDownloadUrl;
        UpdateDownloadButtonState(ModId);
    
        // Clear any previous images and reset banner visibility
        ImageCarousel.Items.Clear();
        BannerImage.IsVisible = false;
    
        // If there are no images to load, finish up early
        if (CurrentMod._aPreviewMedia?._aImages == null || !CurrentMod._aPreviewMedia._aImages.Any())
        {
            loading = false;
            loadingVisual = false;
            ResetVisibility();
            return true;
        }
    
        // Load images sequentially
        foreach (var image in CurrentMod._aPreviewMedia._aImages)
        {
            if (cancellationToken.IsCancellationRequested) return false;
    
            var fullImageUrl = $"{image._sBaseUrl}/{image._sFile}";
    
            var streamResult = await HttpClientHelper.GetStreamAsync(fullImageUrl, cancellationToken);
            if (!streamResult.Succeeded || streamResult.Content == null)
            {
                continue;
            }
    
            // Get the image stream with cancellation support
            await using var stream = streamResult.Content;
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
    
            // Create a bitmap from the memory stream
            var bitmap = new Bitmap(memoryStream);
    
            // Add the bitmap to the image carousel
            ImageCarousel.Items.Add(new { FullImageUrl = bitmap });
    
            // Set the first loaded image as the banner if not already set
            if (!BannerImage.IsVisible)
            {
                BannerImage.IsVisible = true;
                BannerImage.Source = bitmap;
            }
        }
    
        // Reset the loading state once all operations have completed
        loading = false;
        loadingVisual = false;
        ResetVisibility();
        
        return true;
    }

    
    private void UpdateDownloadButtonState(int modId)
    {
        var isInstalled = ModManager.Instance.IsModInstalled(modId);
        InstallButton.Content = isInstalled ? "Installed": "Download and Install";
        InstallButton.IsEnabled = !isInstalled; 
        UnInstallButton.IsVisible = isInstalled;
    }

    /// <summary>
    /// Clears the mod details from the viewer.
    /// </summary>
    private void ClearDetails()
    {
        ImageCarousel.Items.Clear();
        ModTitle.Text = string.Empty;
        AuthorButton.Text = "Unknown";
        LikesCountBox.Text = ViewsCountBox.Text = DownloadsCountBox.Text = "0";
        ModDescriptionHtmlPanel.Text = string.Empty;
        IsVisible = false; 
    }
    
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = await new YesNoWindow()
            .SetMainText($"Do you want to download and install the mod: {CurrentMod._sName}?")
            .AwaitAnswer();
        if (!confirmation) return;
    
        try
        {
            await PrepareToDownloadFile();
            var downloadUrls = CurrentMod.OverrideDownloadUrl != null 
                ? new List<string> { CurrentMod.OverrideDownloadUrl }
                : CurrentMod._aFiles.Select(f => f._sDownloadUrl).ToList();
            if (!downloadUrls.Any())
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Warning)
                    .SetTitleText("Unable to download the mod")
                    .SetInfoText("No downloadable files found for this mod.")
                    .Show();
                return;
            }
            
            var progressWindow = new ProgressWindow($"Downloading {CurrentMod._sName}");
            progressWindow.Show();
            progressWindow.SetExtraText("Loading...");

            var url = downloadUrls.First();
            var fileName = GetFileNameFromUrl(url);
            var filePath = Path.Combine(PathManager.TempModsFolderPath, fileName);
            await DownloadHelper.DownloadToLocationAsync(url, filePath, progressWindow);
            progressWindow.Close();
            var file = Directory.GetFiles(PathManager.TempModsFolderPath).FirstOrDefault();
            if (file == null)
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Warning)
                    .SetTitleText("Unable to download the mod")
                    .SetInfoText("Downloaded file not found.")
                    .Show();
                return;
            }
            var author = "-1";
            if (CurrentMod._aSubmitter?._sName != null)
                author = CurrentMod._aSubmitter._sName;
            
            var modId = CurrentMod._idRow;
            var popup = new TextInputWindow().setLabelText("Mod Name");
            popup.PopulateText(CurrentMod._sName);
            var modName = await popup.ShowDialog();
            if (string.IsNullOrEmpty(modName))
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Warning)
                    .SetTitleText("Mod name Invalid.")
                    .SetInfoText("Please provide a mod name.")
                    .Show();
                return;
            }
            var invalidChars = Path.GetInvalidFileNameChars();
            if (modName.Any(c => invalidChars.Contains(c)))
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Warning)
                    .SetTitleText("Mod name Invalid.")
                    .SetInfoText("Mod name contains invalid characters.")
                    .Show();
                Directory.Delete(PathManager.TempModsFolderPath, true);
                return;
            }
            await ModInstallation.InstallModFromFileAsync(file, modName ,author, modId);
            Directory.Delete(PathManager.TempModsFolderPath, true);
        }
        catch (Exception ex)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Download failed.")
                .SetInfoText("An error occurred during download: " + ex.Message)
                .Show();
        }
        LoadModDetailsAsync(CurrentMod._idRow);
    }

    /// <summary>
    /// Prepares the temporary folder for downloading files.
    /// </summary>
    private static async Task PrepareToDownloadFile()
    {
        var tempFolder = PathManager.TempModsFolderPath;
        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }
        Directory.CreateDirectory(tempFolder);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Extracts the file name from a URL.
    /// </summary>
    private static string GetFileNameFromUrl(string url)
    {
        return Path.GetFileName(new Uri(url).AbsolutePath);
    }

    /// <summary>
    /// Clears the mod details and hides the viewer.
    /// </summary>
    public void HideViewer()
    {
        ClearDetails();
        IsVisible = false;
    }

    private void AuthorLink_Click(object? sender, EventArgs eventArgs)
    {
        var profileURL = CurrentMod._aSubmitter._sProfileUrl;
        ViewUtils.OpenLink(profileURL);
    }

    private void GamebananaLink_Click(object? sender, EventArgs eventArgs)
    {
        ViewUtils.OpenLink(CurrentMod._sProfileUrl);
    }
    private void ReportLink_Click(object? sender, EventArgs eventArgs)
    {
        var url = $"https://gamebanana.com/support/add?s=Mod.{CurrentMod._idRow}";
        ViewUtils.OpenLink(url);
    }
    
    private void UnInstall_Click(object sender, RoutedEventArgs e)
    {
        ModManager.Instance.DeleteModById(CurrentMod._idRow);
        LoadModDetailsAsync(CurrentMod._idRow);
        UpdateDownloadButtonState(CurrentMod._idRow);
    }
}
