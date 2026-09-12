using WheelWizard.Shared.Downloads;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Downloads;

// Presentation adapts download progress and cancellation to the existing dialogs.
// The download service itself has no dependency on Avalonia or global application state.
public static class DownloadPresentation
{
    public static async Task<string?> DownloadToLocationAsync(
        this IDownloadService downloads,
        string url,
        string filePath,
        string windowTitle,
        string extraText = "",
        bool useExactPath = false,
        CancellationToken cancellationToken = default
    )
    {
        var window = new ProgressWindow(windowTitle).SetExtraText(extraText);
        window.Show();
        try
        {
            return await downloads.DownloadToLocationAsync(url, filePath, window, useExactPath, cancellationToken);
        }
        finally
        {
            window.Close();
        }
    }

    public static async Task<string?> DownloadToLocationAsync(
        this IDownloadService downloads,
        string url,
        string filePath,
        ProgressWindow window,
        bool useExactPath = false,
        CancellationToken cancellationToken = default
    )
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.SetCancellationTokenSource(cancellation);
        var progress = new Progress<DownloadProgress>(value =>
        {
            if (value.Retrying)
                window.SetExtraText($"Retrying... Attempt {value.Attempt}");
            else
            {
                window.SetGoal((value.TotalBytes ?? -1) / (1024d * 1024d));
                window.UpdateProgress(value.Percentage);
            }
        });
        try
        {
            var result = await downloads.DownloadAsync(url, filePath, useExactPath, progress, cancellation.Token);
            if (result.IsSuccess)
                return result.Value;

            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Download error")
                .SetInfoText(result.Error.Message)
                .Show();
            return null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            window.MarkCancellationRequested();
            return null;
        }
        finally
        {
            window.SetCancellationTokenSource(null);
        }
    }
}
