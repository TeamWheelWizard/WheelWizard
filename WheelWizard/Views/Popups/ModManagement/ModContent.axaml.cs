using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Helpers;
using WheelWizard.Mods;
using WheelWizard.Services;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Popups.ModManagement;

public record ModItem(Bitmap FullImageUrl);

public partial class ModContent : UserControlBase
{
    private bool loadingVisual;
    private GameBananaModDetails? CurrentMod { get; set; }
    private string? OverrideDownloadUrl { get; set; }

    [Inject]
    private IGameBananaSingletonService GameBananaService { get; set; } = null!;

    [Inject]
    private IModManager ModManager { get; set; } = null!;

    public ModContent()
    {
        InitializeComponent();
        ResetVisibility();
        UnInstallButton.IsVisible = false;

        DescriptionLabel.Text = t("attribute.description") + ":";
        ImageLabel.Text = t("attribute.images") + ":";
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
    /// <param name="cancellationToken">Cancellation token to cancel the loading of the current details.</param>
    public async Task<bool> LoadModDetailsAsync(int ModId, string? newDownloadUrl = null, CancellationToken cancellationToken = default)
    {
        // Check if cancellation has been requested before starting
        if (cancellationToken.IsCancellationRequested)
            return false;
        // Set the UI to show loading state
        loadingVisual = true;
        ResetVisibility();

        // Retrieve the mod details.
        // If GameBananaSearchHandler.GetModDetailsAsync supports cancellation,
        // consider passing the token as a parameter.
        var modDetailsResult = await GameBananaService.GetModDetails(ModId);
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (modDetailsResult.IsFailure)
        {
            CurrentMod = null;
            OverrideDownloadUrl = null;
            NoDetailsView.Title = t("message_error.failed_retrieve_mod.title");
            NoDetailsView.BodyText = modDetailsResult.Error.Message;

            loadingVisual = false;
            ResetVisibility();
            return false;
        }

        CurrentMod = modDetailsResult.Value;

        // Update the UI with mod details
        ModTitle.Text = CurrentMod.Name;
        AuthorButton.Text = CurrentMod.Author.Name;
        LikesCountBox.Text = CurrentMod.LikeCount.ToString();
        ViewsCountBox.Text = CurrentMod.ViewCount.ToString();
        DownloadsCountBox.Text = CurrentMod.DownloadCount.ToString();

        // Wrap the mod description in a div tag so that CSS can be applied
        ModDescriptionHtmlPanel.Text = $"<body>{CurrentMod.Text}</body>";
        OverrideDownloadUrl = newDownloadUrl;
        UpdateDownloadButtonState(ModId);

        // Clear any previous images and reset banner visibility
        ImageCarousel.Items.Clear();
        BannerImage.IsVisible = false;

        // If there are no images to load, finish up early
        if (CurrentMod.PreviewMedia?.Images == null || !CurrentMod.PreviewMedia.Images.Any())
        {
            loadingVisual = false;
            ResetVisibility();
            return true;
        }

        // Load images sequentially
        foreach (var image in CurrentMod.PreviewMedia.Images)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            var fullImageUrl = $"{image.BaseUrl}/{image.File}";

            var streamResult = await HttpClientHelper.GetStreamAsync(fullImageUrl, cancellationToken);
            if (!streamResult.Succeeded || streamResult.Content == null)
                continue;

            // Get the image stream with cancellation support
            await using var stream = streamResult.Content;
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            // Create a bitmap from the memory stream
            var bitmap = new Bitmap(memoryStream);

            // Add the bitmap to the image carousel
            ImageCarousel.Items.Add(new ModItem(bitmap));

            // Set the first loaded image as the banner if not already set
            if (BannerImage.IsVisible)
                continue;

            BannerImage.IsVisible = true;
            BannerImage.Source = bitmap;
        }

        // Reset the loading state once all operations have completed
        loadingVisual = false;
        ResetVisibility();

        return true;
    }

    private void UpdateDownloadButtonState(int modId)
    {
        var isInstalled = ModManager.IsModInstalled(modId);
        InstallButton.Content = isInstalled ? t("state.installed") : t("action.download_and_install");
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
        AuthorButton.Text = t("state.unknown");
        LikesCountBox.Text = ViewsCountBox.Text = DownloadsCountBox.Text = "0";
        ModDescriptionHtmlPanel.Text = string.Empty;
        IsVisible = false;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentMod == null)
            return;

        var confirmation = await new YesNoWindow()
            .SetMainText(Humanizer.ReplaceDynamic(t("question.install_mod.title"), CurrentMod.Name) ?? CurrentMod.Name)
            .AwaitAnswer();
        if (!confirmation)
            return;

        var installResult = await DownloadAndInstallCurrentModAsync();
        if (installResult.IsFailure)
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Error_ModDownloadFailed, null, [installResult.Error.Message]);
        }
        else
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Message)
                .SetTitleText(t("message_success.mod_installed.title"))
                .SetInfoText(Humanizer.ReplaceDynamic(t("message_success.mod_installed.extra"), CurrentMod.Name)!)
                .Show();
        }

        _ = LoadModDetailsAsync(CurrentMod.Id);
    }

    private async Task<OperationResult> DownloadAndInstallCurrentModAsync()
    {
        if (CurrentMod == null)
            return Ok();

        var prepareResult = await PrepareToDownloadFile();
        if (prepareResult.IsFailure)
            return prepareResult.Error;

        var downloadUrls = OverrideDownloadUrl != null ? [OverrideDownloadUrl] : CurrentMod.Files.Select(f => f.DownloadUrl).ToList();
        if (!downloadUrls.Any())
            return Fail("No downloadable files were found for this mod.");

        var progressWindow = new ProgressWindow(Humanizer.ReplaceDynamic(t("progress.downloading_mod"), CurrentMod.Name)!);
        progressWindow.Show();
        progressWindow.SetExtraText(t("state.loading"));

        var url = downloadUrls.First();
        var fileName = GetFileNameFromUrl(url);
        var filePath = Path.Combine(PathManager.TempModsFolderPath, fileName);
        var downloadResult = await DownloadModFileAsync(url, filePath, progressWindow);
        progressWindow.Close();

        if (downloadResult.IsFailure)
            return downloadResult.Error;

        var downloadedFilePath = downloadResult.Value;
        if (string.IsNullOrWhiteSpace(downloadedFilePath))
        {
            TryDeleteTempModsFolder();
            return Ok();
        }

        if (!File.Exists(downloadedFilePath))
            return Fail(t("message_warning.unable_download_mod.extra"));

        var popup = new TextInputWindow()
            .SetMainText(t("attribute.name"))
            .SetInitialText(CurrentMod.Name)
            .SetValidation(ModManager.ValidateModName)
            .SetPlaceholderText(t("placeholder.enter_mod_name"));
        var modName = await popup.ShowDialog();
        if (modName == null)
        {
            TryDeleteTempModsFolder();
            return Ok();
        }

        if (string.IsNullOrEmpty(modName))
        {
            TryDeleteTempModsFolder();
            return Fail("Mod name cannot be empty.");
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (modName.Any(c => invalidChars.Contains(c)))
        {
            TryDeleteTempModsFolder();
            return Fail(t("message_warning.mod_name_invalid.extra"));
        }

        var installResult = await ModManager.InstallModFromFileAsync(downloadedFilePath, modName, CurrentMod.Author.Name, CurrentMod.Id);
        if (installResult.IsFailure)
            return installResult.Error;

        return TryDeleteTempModsFolder();
    }

    /// <summary>
    /// Prepares the temporary folder for downloading files.
    /// </summary>
    private static async Task<OperationResult> PrepareToDownloadFile()
    {
        try
        {
            var tempFolder = PathManager.TempModsFolderPath;
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, true);

            Directory.CreateDirectory(tempFolder);
            await Task.CompletedTask;
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to prepare the temporary download folder: {ex.Message}", Exception = ex };
        }
    }

    private static OperationResult TryDeleteTempModsFolder()
    {
        try
        {
            if (Directory.Exists(PathManager.TempModsFolderPath))
                Directory.Delete(PathManager.TempModsFolderPath, true);

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to clean up the temporary download folder: {ex.Message}", Exception = ex };
        }
    }

    private static async Task<OperationResult<string?>> DownloadModFileAsync(string url, string filePath, ProgressWindow progressWindow)
    {
        try
        {
            return await DownloadHelper.DownloadToLocationAsync(url, filePath, progressWindow);
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to download mod file: {ex.Message}", Exception = ex };
        }
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
        var profileUrl = CurrentMod?.Author.ProfileUrl;
        if (profileUrl != null)
            ViewUtils.OpenLink(profileUrl);
    }

    private void GameBananaLink_Click(object? sender, EventArgs eventArgs)
    {
        var profileUrl = CurrentMod?.ProfileUrl;
        if (profileUrl != null)
            ViewUtils.OpenLink(profileUrl);
    }

    private void ReportLink_Click(object? sender, EventArgs eventArgs)
    {
        var url = $"https://gamebanana.com/support/add?s=Mod.{CurrentMod?.Id}";
        ViewUtils.OpenLink(url);
    }

    private async void UnInstall_Click(object sender, RoutedEventArgs e)
    {
        var id = CurrentMod?.Id;
        if (id is null or -1)
            return;

        var deleteResult = await ModManager.DeleteModByIdAsync(id.Value);
        if (deleteResult.IsFailure)
        {
            MessageTranslationHelper.ShowMessage(deleteResult.Error);
            return;
        }

        await LoadModDetailsAsync(id.Value);
    }
}
