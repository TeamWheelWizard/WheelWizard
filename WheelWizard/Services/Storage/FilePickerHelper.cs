using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace WheelWizard.Services;

public static class FilePickerHelper
{
    /// <summary>
    /// Opens a file picker with the specified options.
    /// </summary>
    /// <param name="fileType">The file type filter to use.</param>
    /// <param name="allowMultiple">Whether multiple file selection is allowed.</param>
    /// <param name="title">The title of the file picker dialog.</param>
    /// <returns>A list of selected file paths or an empty list if no files were selected.</returns>
    public static async Task<List<string>> OpenFilePickerAsync(
        FilePickerFileType fileType,
        bool allowMultiple = true,
        string title = "Select Files"
    )
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return [];

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = new List<FilePickerFileType> { fileType },
        };

        var selectedFiles = await storageProvider.MainWindow.StorageProvider.OpenFilePickerAsync(options);

        return selectedFiles?.Select(TryResolveLocalPath).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).ToList()
            ?? [];
    }

    public static async Task<string?> OpenSingleFileAsync(string title, IEnumerable<FilePickerFileType> fileTypes)
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return null;

        var topLevel = TopLevel.GetTopLevel(storageProvider.MainWindow);
        if (topLevel?.StorageProvider == null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new()
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = fileTypes.ToList(),
            }
        );

        if (files == null)
            return null;

        foreach (var file in files)
        {
            var path = TryResolveLocalPath(file);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        return null;
    }

    public static async Task<List<string>> OpenMultipleFilesAsync(string title, IEnumerable<FilePickerFileType> fileTypes)
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return null;

        var topLevel = TopLevel.GetTopLevel(storageProvider.MainWindow);
        if (topLevel?.StorageProvider == null)
            return [];

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new()
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = fileTypes.ToList(),
            }
        );

        return files?.Select(TryResolveLocalPath).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).ToList() ?? [];
    }

    public static async Task<IReadOnlyList<IStorageFolder?>> SelectFolderAsync(string title, IStorageFolder? suggestedStartLocation = null)
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return null;

        var topLevel = TopLevel.GetTopLevel(storageProvider.MainWindow);
        if (topLevel?.StorageProvider == null)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new()
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStartLocation,
            }
        );

        return folders;
    }

    public static void OpenFolderInFileManager(string folderPath)
    {
        string? openExecutable;
        if (OperatingSystem.IsWindows())
        {
            openExecutable = "explorer.exe";
        }
        else if (OperatingSystem.IsLinux())
        {
            openExecutable = "xdg-open";
        }
        else if (OperatingSystem.IsMacOS())
        {
            openExecutable = "open";
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported operating system.");
        }

        var info = new ProcessStartInfo(openExecutable)
        {
            // Ensures the folder path is escaped properly
            ArgumentList = { folderPath },
        };

        Process.Start(info);
    }

    public static async Task<string?> SaveFileAsync(
        string title,
        IEnumerable<FilePickerFileType> fileTypes,
        string defaultFileName = "untitled",
        IStorageFolder? suggestedStartLocation = null
    )
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return null;

        var topLevel = TopLevel.GetTopLevel(storageProvider.MainWindow);
        if (topLevel?.StorageProvider == null)
            return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedStartLocation = suggestedStartLocation,
                SuggestedFileName = defaultFileName,
                FileTypeChoices = fileTypes.ToList(),
                ShowOverwritePrompt = true,
            }
        );

        if (file == null)
            return null;

        return TryResolveLocalPath(file);
    }

    public static string? TryResolveLocalPath(IStorageItem? item)
    {
        if (item == null)
            return null;

        try
        {
            var localPath = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
                return localPath;
        }
        catch
        {
            // Some platforms might throw if local paths are unsupported; ignore and fall back to URI inspection.
        }

        var uri = item.Path;
        if (uri != null)
        {
            if (uri.IsAbsoluteUri)
            {
                try
                {
                    return uri.LocalPath;
                }
                catch (InvalidOperationException)
                {
                    // Ignore and fall through to raw string handling.
                }
            }

            var raw = uri.ToString();
            if (!string.IsNullOrWhiteSpace(raw) && Path.IsPathRooted(raw))
                return raw;
        }

        return null;
    }
}
