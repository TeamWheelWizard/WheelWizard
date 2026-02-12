using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Helpers;

// todo: Delete this static class and write a service for it.
public static class DownloadHelper
{
    private const int MaxRetries = 5;
    private const int TimeoutInSeconds = 30; // Adjust as needed

    public static async Task<string?> DownloadToLocationAsync(
        string url,
        string filePath,
        string windowTitle,
        string extraText = "",
        bool ForceGivenFilePath = false,
        CancellationToken cancellationToken = default
    )
    {
        using var linkedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressWindow = new ProgressWindow(windowTitle).SetExtraText(extraText).SetCancellationTokenSource(linkedCancellationToken);
        progressWindow.Show();

        try
        {
            return await DownloadToLocationAsync(url, filePath, progressWindow, ForceGivenFilePath, linkedCancellationToken.Token);
        }
        finally
        {
            progressWindow.Close();
        }
    }

    public static async Task<string?> DownloadToLocationAsync(
        string url,
        string tempFile,
        ProgressWindow progressPopupWindow,
        bool ForceGivenFilePath = false,
        CancellationToken cancellationToken = default
    )
    {
        using var linkedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var effectiveCancellationToken = linkedCancellationToken.Token;
        progressPopupWindow.SetCancellationTokenSource(linkedCancellationToken);

        var directory = Path.GetDirectoryName(tempFile)!;
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var attempt = 0;
        var success = false;
        string resolvedFilePath = tempFile;

        try
        {
            while (attempt < MaxRetries && !success)
            {
                if (effectiveCancellationToken.IsCancellationRequested)
                {
                    progressPopupWindow.MarkCancellationRequested();
                    return null;
                }

                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(TimeoutInSeconds);
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, effectiveCancellationToken);
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage == null || response.RequestMessage.RequestUri == null)
                    {
                        return null;
                    }

                    if (!ForceGivenFilePath)
                    {
                        var finalUrl = response.RequestMessage.RequestUri.ToString();

                        // Check for filename in Content-Disposition or fallback to URL
                        var contentDisposition = response.Content.Headers.ContentDisposition;
                        var fileName = contentDisposition?.FileName?.Trim('"') ?? Path.GetFileName(new Uri(url).AbsolutePath);
                        fileName = Path.ChangeExtension(fileName, Path.GetExtension(finalUrl));

                        // Add extension if missing in file path
                        if (!Path.HasExtension(fileName))
                        {
                            var urlExtension = Path.GetExtension(new Uri(url).AbsolutePath);
                            if (!string.IsNullOrEmpty(urlExtension))
                            {
                                fileName += urlExtension;
                            }
                        }

                        // Update resolvedFilePath with resolved fileName
                        resolvedFilePath = Path.Combine(directory, fileName);
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    progressPopupWindow.SetGoal(totalBytes / (1024.0 * 1024.0));

                    await using var downloadStream = await response.Content.ReadAsStreamAsync(effectiveCancellationToken);
                    await using var fileStream = new FileStream(
                        resolvedFilePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 8192,
                        useAsync: true
                    );
                    var downloadedBytes = 0L;
                    const int bufferSize = 8192;
                    var buffer = new byte[bufferSize];
                    int bytesRead;

                    while ((bytesRead = await downloadStream.ReadAsync(buffer.AsMemory(0, bufferSize), effectiveCancellationToken)) != 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), effectiveCancellationToken);
                        downloadedBytes += bytesRead;

                        var progress = totalBytes == -1 ? 0 : (int)((float)downloadedBytes / totalBytes * 100);
                        progressPopupWindow.UpdateProgress(progress);
                    }

                    success = true; // Download completed successfully
                }
                catch (TaskCanceledException ex)
                    when (!effectiveCancellationToken.IsCancellationRequested && !ex.CancellationToken.IsCancellationRequested)
                {
                    attempt++;
                    if (attempt >= MaxRetries)
                    {
                        new MessageBoxWindow()
                            .SetMessageType(MessageBoxWindow.MessageType.Error)
                            .SetTitleText("Download timed out")
                            .SetInfoText($"The download timed out after {MaxRetries} attempts.")
                            .Show();
                        break;
                    }

                    var delay = (int)Math.Pow(2, attempt) * 1000;
                    try
                    {
                        await Task.Delay(delay, effectiveCancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        progressPopupWindow.MarkCancellationRequested();
                        break;
                    }
                    progressPopupWindow.SetExtraText($"Retrying... Attempt {attempt + 1} of {MaxRetries}");
                }
                catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
                {
                    progressPopupWindow.MarkCancellationRequested();
                    break;
                }
                catch (HttpRequestException ex)
                {
                    attempt++;
                    if (attempt >= MaxRetries)
                    {
                        new MessageBoxWindow()
                            .SetMessageType(MessageBoxWindow.MessageType.Error)
                            .SetTitleText("Tried to many times")
                            .SetInfoText($"An HTTP error occurred after {MaxRetries} attempts: {ex.Message}")
                            .Show();
                        break;
                    }
                    var delay = (int)Math.Pow(2, attempt) * 1000;
                    try
                    {
                        await Task.Delay(delay, effectiveCancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        progressPopupWindow.MarkCancellationRequested();
                        break;
                    }
                    progressPopupWindow.SetExtraText($"Retrying... Attempt {attempt + 1} of {MaxRetries}");
                }
                catch (Exception ex)
                {
                    new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText("Download error")
                        .SetInfoText($"An error occurred while downloading the file: {ex.Message}")
                        .Show();
                    break;
                }
            }

            if (!success && File.Exists(resolvedFilePath))
                File.Delete(resolvedFilePath);

            return success ? resolvedFilePath : null;
        }
        finally
        {
            progressPopupWindow.SetCancellationTokenSource(null);
        }
    }
}
