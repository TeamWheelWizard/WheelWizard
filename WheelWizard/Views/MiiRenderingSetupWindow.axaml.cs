using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Services;
using WheelWizard.Shared.DependencyInjection;

namespace WheelWizard.Views;

public partial class MiiRenderingSetupWindow : BaseWindow
{
    private readonly TaskCompletionSource<bool> _completionSource = new();
    private CancellationTokenSource? _downloadCancellationTokenSource;
    private bool _downloadCompleted;

    [Inject]
    private IMiiRenderingResourceInstaller ResourceInstaller { get; set; } = null!;

    [Inject]
    private ILogger<MiiRenderingSetupWindow> Logger { get; set; } = null!;

    protected override Control InteractionOverlay => DisabledOverlay;
    protected override Control InteractionContent => MainContent;

    public MiiRenderingSetupWindow()
    {
        InitializeComponent();
        AddLayer();

        Title = "Wheel Wizard";
        PathTextBlock.Text = PathManager.MiiRenderingResourceFilePath;
        StatusTextBlock.Text = "Download the Mii rendering resource to continue.";
        ProgressTextBlock.Text = "Ready to install.";
    }

    public Task<bool> ShowAndAwaitCompletionAsync()
    {
        Show();
        return _completionSource.Task;
    }

    private async void DownloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadCancellationTokenSource != null)
            return;

        ErrorTextBlock.IsVisible = false;
        SetBusyState(true, "Downloading required file...");
        DownloadProgressBar.Value = 0;
        _downloadCancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<MiiRenderingInstallerProgress>(UpdateProgress);

        try
        {
            var result = await ResourceInstaller.DownloadAndInstallAsync(progress, _downloadCancellationTokenSource.Token);
            if (result.IsFailure)
            {
                Logger.LogWarning("Mii rendering setup failed: {Message}", result.Error?.Message);
                ShowError(result.Error?.Message ?? "Failed to install the Mii rendering resource.");
                return;
            }

            _downloadCompleted = true;
            _completionSource.TrySetResult(true);
            Close();
        }
        finally
        {
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;

            if (!_downloadCompleted)
                SetBusyState(false, "Download the Mii rendering resource to continue.");
        }
    }

    private void UpdateProgress(MiiRenderingInstallerProgress progress)
    {
        StatusTextBlock.Text = progress.Stage;

        if (progress.TotalBytes is > 0)
        {
            var percentage = Math.Clamp((progress.BytesDownloaded / (double)progress.TotalBytes.Value) * 100.0, 0, 100);
            DownloadProgressBar.Value = percentage;
            ProgressTextBlock.Text =
                $"{percentage:F0}% ({FormatMegabytes(progress.BytesDownloaded)} / {FormatMegabytes(progress.TotalBytes.Value)})";
            return;
        }

        DownloadProgressBar.Value = 0;
        ProgressTextBlock.Text = progress.BytesDownloaded > 0 ? $"{FormatMegabytes(progress.BytesDownloaded)} downloaded" : "Working...";
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _downloadCancellationTokenSource?.Cancel();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellationTokenSource?.Cancel();
        _completionSource.TrySetResult(_downloadCompleted);
        RemoveLayer();
        base.OnClosed(e);
    }

    private void SetBusyState(bool busy, string statusText)
    {
        StatusTextBlock.Text = statusText;
        DownloadButton.IsEnabled = !busy;
        CloseButton.IsEnabled = true;
        CloseButton.Text = busy ? "Close and Exit" : "Close";
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.IsVisible = true;
        SetBusyState(false, "Download the Mii rendering resource to continue.");
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / 1024d / 1024d:F2} MB";
}
