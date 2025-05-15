using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Helpers;

public static class DownloadHelper
{
    private const int MaxRetries = 5;
    private const int TimeoutInSeconds = 30; // Adjust as needed

    public static async Task<string> DownloadToLocationAsync(
        string url,
        string filePath,
        string windowTitle,
        string extraText = "",
        bool ForceGivenFilePath = false
    )
    {
        var progressWindow = new ProgressWindow(windowTitle).SetExtraText(extraText);
        progressWindow.Show();
        var toLocationAsync = await DownloadToLocationAsync(url, filePath, progressWindow, ForceGivenFilePath);
        progressWindow.Close();
        return toLocationAsync;
    }

    public static async Task<string?> DownloadToLocationAsync(
        string url,
        string tempFile,
        ProgressWindow progressPopupWindow,
        bool ForceGivenFilePath = false
    )
    {
        var directory = Path.GetDirectoryName(tempFile)!;
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var attempt = 0;
        var success = false;
        string resolvedFilePath = tempFile;

        while (attempt < MaxRetries && !success)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(TimeoutInSeconds);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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

                await using var downloadStream = await response.Content.ReadAsStreamAsync();
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

                while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, bufferSize)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    var progress = totalBytes == -1 ? 0 : (int)((float)downloadedBytes / totalBytes * 100);
                    progressPopupWindow.UpdateProgress(progress);
                }

                success = true; // Download completed successfully
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                attempt++;
                if (attempt >= MaxRetries)
                {
                    new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText("Download timed out")
                        .SetInfoText($"The download timed out after {MaxRetries} attempts.");
                    break;
                }

                var delay = (int)Math.Pow(2, attempt) * 1000;
                await Task.Delay(delay);
                progressPopupWindow.SetExtraText($"Retrying... Attempt {attempt + 1} of {MaxRetries}");
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
                await Task.Delay(delay);
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

        return resolvedFilePath;
    }
}
