using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Services.Installation;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Popups.ModManagement;

public record ModItem(Bitmap FullImageUrl);

public partial class ModContent : UserControlBase
{
    private bool loading;
    private bool loadingVisual;
    private GameBananaModDetails? CurrentMod { get; set; }
    private string? OverrideDownloadUrl { get; set; }

    [Inject]
    private IGameBananaSingletonService GameBananaService { get; set; } = null!;

    public ModContent()
    {
        InitializeComponent();
        ResetVisibility();
        UnInstallButton.IsVisible = false;

        DescriptionLabel.Text = Common.Attribute_Description + ":";
        ImageLabel.Text = Common.Attribute_Images + ":";
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
        loading = true;
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
            NoDetailsView.Title = Phrases.MessageError_FailedRetrieveMod_Title;
            NoDetailsView.BodyText = modDetailsResult.Error.Message;

            loading = false;
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
            loading = false;
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
        loading = false;
        loadingVisual = false;
        ResetVisibility();

        return true;
    }

    private void UpdateDownloadButtonState(int modId)
    {
        var isInstalled = ModManager.Instance.IsModInstalled(modId);
        InstallButton.Content = isInstalled ? Common.State_Installed : Common.Action_DownloadAndInstall;
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
        AuthorButton.Text = Common.State_Unknown;
        LikesCountBox.Text = ViewsCountBox.Text = DownloadsCountBox.Text = "0";
        ModDescriptionHtmlPanel.Text = string.Empty;
        IsVisible = false;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentMod == null)
            return;

        var confirmation = await new YesNoWindow()
            .SetMainText(Humanizer.ReplaceDynamic(Phrases.Question_InstallMod_Title, CurrentMod.Name) ?? CurrentMod.Name)
            .AwaitAnswer();
        if (!confirmation)
            return;

        try
        {
            await PrepareToDownloadFile();
            var downloadUrls = OverrideDownloadUrl != null ? [OverrideDownloadUrl] : CurrentMod.Files.Select(f => f.DownloadUrl).ToList();
            if (!downloadUrls.Any())
            {
                MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_UnableToDownloadMod_Files);
                return;
            }

            var progressWindow = new ProgressWindow($"Downloading {CurrentMod.Name}");
            progressWindow.Show();
            progressWindow.SetExtraText(Common.State_Loading);

            var url = downloadUrls.First();
            var fileName = GetFileNameFromUrl(url);
            var filePath = Path.Combine(PathManager.TempModsFolderPath, fileName);
            await DownloadHelper.DownloadToLocationAsync(url, filePath, progressWindow);
            progressWindow.Close();
            var file = Directory.GetFiles(PathManager.TempModsFolderPath).FirstOrDefault();
            if (file == null)
            {
                MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_UnableToDownloadMod_Files);
                return;
            }

            var author = CurrentMod.Author.Name;
            var modId = CurrentMod.Id;
            var popup = new TextInputWindow()
                .SetMainText(Common.Attribute_Name)
                .SetInitialText(CurrentMod.Name)
                .SetValidation(ModManager.Instance.ValidateModName)
                .SetPlaceholderText(Phrases.Placeholder_EnterModName);
            var modName = await popup.ShowDialog();
            if (modName == null)
                return;

            if (string.IsNullOrEmpty(modName))
            {
                MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_ModNameCantEmpty);
                return;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            if (modName.Any(c => invalidChars.Contains(c)))
            {
                MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_ModNameInvalid);
                Directory.Delete(PathManager.TempModsFolderPath, true);
                return;
            }

            await ModInstallation.InstallModFromFileAsync(file, modName, author, modId);
            Directory.Delete(PathManager.TempModsFolderPath, true);
        }
        catch (Exception ex)
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Error_ModDownloadFailed, null, [ex.Message]);
        }

        _ = LoadModDetailsAsync(CurrentMod.Id);
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

        ModManager.Instance.DeleteModById(id.Value);
        await LoadModDetailsAsync(id.Value);
    }
}
