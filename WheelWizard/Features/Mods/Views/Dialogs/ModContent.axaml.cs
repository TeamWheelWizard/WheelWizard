using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Mods.ViewModels;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Downloads.Presentation;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;

namespace WheelWizard.Mods.Views.Dialogs;

public partial class ModContent : UserControl
{
    private IModManager ModManager { get; }

    private IGameBananaSingletonService GameBananaService { get; }

    private IDownloadService Downloads { get; }

    private IGameBananaMediaService Media { get; }

    private IModPaths ModPaths { get; }

    private IModOperationPresentation ModPresentation { get; }

    private bool loadingVisual;
    private CancellationTokenSource? _loadCancellation;
    private GameBananaModDetails? CurrentMod { get; set; }
    private string? OverrideDownloadUrl { get; set; }

    public ModContent(
        IModManager modManager,
        IGameBananaSingletonService gameBananaService,
        IDownloadService downloads,
        IGameBananaMediaService media,
        IModPaths modPaths,
        IModOperationPresentation modPresentation
    )
    {
        ModManager = modManager;
        GameBananaService = gameBananaService;
        Downloads = downloads;
        Media = media;
        ModPaths = modPaths;
        ModPresentation = modPresentation;
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
    /// <param name="cancellationToken">Cancellation token to cancel the loading of the current details.</param>
    public async Task<bool> LoadModDetailsAsync(int ModId, string? newDownloadUrl = null, CancellationToken cancellationToken = default)
    {
        _loadCancellation?.Cancel();
        using var load = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = load;
        var token = load.Token;
        loadingVisual = true;
        CurrentMod = null;
        ModTitle.Text = t("state.loading");
        ReleaseImages();
        ResetVisibility();
        try
        {
            token.ThrowIfCancellationRequested();
            var modDetailsResult = await GameBananaService.GetModDetails(ModId);
            token.ThrowIfCancellationRequested();
            if (modDetailsResult.IsFailure)
            {
                OverrideDownloadUrl = null;
                NoDetailsView.Title = t("message_error.failed_retrieve_mod.title");
                NoDetailsView.BodyText = modDetailsResult.Error.Message;
                return false;
            }

            CurrentMod = modDetailsResult.Value;
            ModTitle.Text = CurrentMod.Name;
            AuthorName.Text = CurrentMod.Author.Name;
            CategoryBadge.Text = CurrentMod.Category.Name;
            CategoryBadge.IsVisible = !string.IsNullOrWhiteSpace(CurrentMod.Category.Name);
            VersionBadge.Text = CurrentMod.Version;
            VersionBadge.IsVisible = !string.IsNullOrWhiteSpace(CurrentMod.Version);
            LikesCountBox.Text = CurrentMod.LikeCount.ToString();
            ViewsCountBox.Text = CurrentMod.ViewCount.ToString();
            DownloadsCountBox.Text = CurrentMod.DownloadCount.ToString();
            ModDescriptionHtmlPanel.Text = $"<body>{CurrentMod.Text}</body>";
            OverrideDownloadUrl = newDownloadUrl;
            UpdateDownloadButtonState(ModId);
            DetailsScroll.Offset = default;

            // Details and actions are usable while the gallery loads.
            loadingVisual = false;
            ResetVisibility();
            var images = CurrentMod.PreviewMedia?.Images;
            if (images == null || images.Count == 0)
                return true;

            ImagesSection.IsVisible = true;
            ImagesLoading.IsVisible = true;
            foreach (var _ in images)
                ImageCarousel.Items.Add(new ModScreenshotViewModel($"{t("attribute.images")} {ImageCarousel.Items.Count + 1}"));
            ImageCarousel.SelectedIndex = 0;
            UpdateGallerySelection();
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                var screenshot = (ModScreenshotViewModel)ImageCarousel.Items[i]!;
                token.ThrowIfCancellationRequested();
                var imageResult = await Media.GetImageAsync($"{image.BaseUrl}/{image.File}", token);
                token.ThrowIfCancellationRequested();
                if (imageResult.IsFailure)
                {
                    screenshot.Complete(null);
                    UpdateGallerySelection();
                    continue;
                }

                Bitmap bitmap;
                try
                {
                    using var stream = new MemoryStream(imageResult.Value, writable: false);
                    bitmap = new Bitmap(stream);
                }
                catch (ArgumentException)
                {
                    // A bad remote image must not prevent viewing or installing the mod.
                    screenshot.Complete(null);
                    UpdateGallerySelection();
                    continue;
                }
                screenshot.Complete(bitmap);
                UpdateGallerySelection();
            }
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, load))
            {
                _loadCancellation = null;
                loadingVisual = false;
                ImagesLoading.IsVisible = false;
                ImagesSection.IsVisible = ImageCarousel.Items.Count > 0;
                ResetVisibility();
            }
        }
    }

    private void ImageCarousel_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateGallerySelection();
        if (ImageCarousel.SelectedIndex >= 0)
            ImageCarousel.ScrollIntoView(ImageCarousel.SelectedIndex);
    }

    // Reveal thumbnails within their strip without scrolling the whole details page.
    private void ImageCarousel_RequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e) => e.Handled = true;

    private void PreviousImage_Click(object? sender, RoutedEventArgs e) => ImageCarousel.SelectedIndex--;

    private void NextImage_Click(object? sender, RoutedEventArgs e) => ImageCarousel.SelectedIndex++;

    private void UpdateGallerySelection()
    {
        var index = ImageCarousel.SelectedIndex;
        var count = ImageCarousel.Items.Count;
        var selected = ImageCarousel.SelectedItem as ModScreenshotViewModel;
        BannerImage.Source = selected?.Image;
        // Keep the image's natural aspect ratio at the preferred height; XAML caps wide images to the viewport.
        BannerFrame.Width = selected?.Image is { } image ? image.Size.Width / image.Size.Height * (BannerFrame.Height - 2) + 2 : 464;
        BannerFrame.IsVisible = selected != null;
        BannerLoading.IsVisible = selected?.IsLoading == true;
        BannerUnavailable.IsVisible = selected?.HasFailed == true;
        ImagePosition.Text = count == 0 ? "" : $"{index + 1} / {count}";
        PreviousImage.IsEnabled = index > 0;
        NextImage.IsEnabled = index >= 0 && index < count - 1;
    }

    private void ReleaseImages()
    {
        BannerImage.Source = null;
        BannerFrame.IsVisible = false;
        ImagesSection.IsVisible = false;
        var images = ImageCarousel.Items.OfType<ModScreenshotViewModel>().ToArray();
        ImageCarousel.Items.Clear();
        UpdateGallerySelection();
        foreach (var image in images)
            image.Dispose();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _loadCancellation?.Cancel();
        ReleaseImages();
        base.OnDetachedFromVisualTree(e);
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
        _loadCancellation?.Cancel();
        ReleaseImages();
        ModTitle.Text = string.Empty;
        AuthorName.Text = t("state.unknown");
        LikesCountBox.Text = ViewsCountBox.Text = DownloadsCountBox.Text = "0";
        ModDescriptionHtmlPanel.Text = string.Empty;
        IsVisible = false;
    }

    private async void Install_Click(object sender, RoutedEventArgs e) => await InstallCurrentModAsync();

    private async Task InstallCurrentModAsync()
    {
        var mod = CurrentMod;
        if (mod == null || TopLevel.GetTopLevel(this) is not BaseWindow { IsVisible: true } owner)
            return;

        using var lifetime = new CancellationTokenSource();
        var surface = this.FindAncestorOfType<ModalContent>();
        void OwnerClosed(object? sender, EventArgs e) => lifetime.Cancel();
        void ContentDetached(object? sender, VisualTreeAttachmentEventArgs e) => lifetime.Cancel();
        void SurfaceClosing(object? sender, EventArgs e) => lifetime.Cancel();
        owner.Closed += OwnerClosed;
        DetachedFromVisualTree += ContentDetached;
        if (surface != null)
            surface.Closing += SurfaceClosing;
        try
        {
            var question = new YesNoWindow().SetMainText(t("question.install_mod.title", new { modName = mod.Name }) ?? mod.Name);
            question.SetModalOwner(this);
            var confirmation = await question.AwaitAnswer();
            lifetime.Token.ThrowIfCancellationRequested();
            if (!confirmation)
                return;

            var installResult = await DownloadAndInstallCurrentModAsync(mod, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (installResult.IsFailure)
            {
                MessageTranslationHelper.ShowMessage(
                    MessageTranslation.Error_ModDownloadFailed,
                    null,
                    new { error = installResult.Error.Message }
                );
            }
            else
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Message)
                    .SetTitleText(t("message_success.mod_installed.title"))
                    .SetInfoText(t("message_success.mod_installed.extra", new { modName = mod.Name })!)
                    .SetModalOwner(this)
                    .Show();
            }
            await LoadModDetailsAsync(mod.Id, cancellationToken: lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            owner.Closed -= OwnerClosed;
            DetachedFromVisualTree -= ContentDetached;
            if (surface != null)
                surface.Closing -= SurfaceClosing;
        }
    }

    private async Task<OperationResult> DownloadAndInstallCurrentModAsync(GameBananaModDetails mod, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prepareResult = await PrepareToDownloadFile();
        cancellationToken.ThrowIfCancellationRequested();
        if (prepareResult.IsFailure)
            return prepareResult.Error;

        var downloadUrls =
            OverrideDownloadUrl != null
                ? [OverrideDownloadUrl]
                : mod.Files?.Select(f => f.DownloadUrl).ToList() ?? mod.ArchivedFiles?.Select(f => f.DownloadUrl).ToList() ?? [];
        if (downloadUrls.Count == 0)
            return Fail("No downloadable files were found for this mod.");

        var progressWindow = new ProgressWindow(t("progress.downloading_mod", new { modName = mod.Name })!);
        progressWindow.SetModalOwner(this);
        progressWindow.Show();
        progressWindow.SetExtraText(t("state.loading"));

        var url = downloadUrls.First();
        var fileName = GetFileNameFromUrl(url);
        var filePath = Path.Combine(ModPaths.DownloadFolderPath, fileName);
        OperationResult<string?> downloadResult;
        try
        {
            downloadResult = await DownloadModFileAsync(url, filePath, progressWindow, cancellationToken);
        }
        finally
        {
            progressWindow.Close();
        }
        cancellationToken.ThrowIfCancellationRequested();

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
            .SetInitialText(mod.Name)
            .SetValidation(ModManager.ValidateModName)
            .SetPlaceholderText(t("placeholder.enter_mod_name"));
        popup.SetModalOwner(this);
        var modName = await popup.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
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

        var installResult = await ModPresentation.RunAsync(
            (progress, _) => ModManager.InstallModFromFileAsync(downloadedFilePath, modName, mod.Author.Name, mod.Id, progress)
        );
        cancellationToken.ThrowIfCancellationRequested();
        if (installResult.IsFailure)
            return installResult.Error;

        return TryDeleteTempModsFolder();
    }

    /// <summary>
    /// Prepares the temporary folder for downloading files.
    /// </summary>
    private async Task<OperationResult> PrepareToDownloadFile()
    {
        try
        {
            var tempFolder = ModPaths.DownloadFolderPath;
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

    private OperationResult TryDeleteTempModsFolder()
    {
        try
        {
            if (Directory.Exists(ModPaths.DownloadFolderPath))
                Directory.Delete(ModPaths.DownloadFolderPath, true);

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to clean up the temporary download folder: {ex.Message}", Exception = ex };
        }
    }

    private async Task<OperationResult<string?>> DownloadModFileAsync(
        string url,
        string filePath,
        ProgressWindow progressWindow,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await Downloads.DownloadToLocationAsync(url, filePath, progressWindow, cancellationToken: cancellationToken);
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

    private void AuthorLink_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var profileUrl = CurrentMod?.Author.ProfileUrl;
        if (profileUrl != null)
            ViewUtils.OpenLink(profileUrl);
    }

    private void GameBananaLink_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var profileUrl = CurrentMod?.ProfileUrl;
        if (profileUrl != null)
            ViewUtils.OpenLink(profileUrl);
    }

    private void ReportLink_Click(object? sender, RoutedEventArgs eventArgs)
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
