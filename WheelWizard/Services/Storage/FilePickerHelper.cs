using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System.Diagnostics;

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
    public static async Task<List<string>> OpenFilePickerAsync(FilePickerFileType fileType, bool allowMultiple = true, string title = "Select Files")
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return new List<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = new List<FilePickerFileType> { fileType }
        };

        var selectedFiles = await storageProvider.MainWindow.StorageProvider.OpenFilePickerAsync(options);

        return selectedFiles?.Select(file => file.Path.LocalPath).ToList() ?? new List<string>();
    }
    
    public static async Task<string?> OpenSingleFileAsync(string title, IEnumerable<FilePickerFileType> fileTypes)
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return null;
        
        var topLevel = TopLevel.GetTopLevel(storageProvider.MainWindow);
        if (topLevel?.StorageProvider == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes.ToList()
        });

        return files?.FirstOrDefault()?.Path.LocalPath;
    }
    
    public static async Task<List<string>> OpenMultipleFilesAsync(string title, IEnumerable<FilePickerFileType> fileTypes)
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return null;
        
        var topLevel = TopLevel.GetTopLevel(storageProvider.MainWindow);
        if (topLevel?.StorageProvider == null) return new List<string>();

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = fileTypes.ToList()
        });

        return files?.Select(file => file.Path.LocalPath).ToList() ?? new List<string>();
    }
    
    public static async Task<IReadOnlyList<IStorageFolder?>> SelectFolderAsync(string title, IStorageFolder? suggestedStartLocation = null)
    {
        var storageProvider = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (storageProvider == null)
            return null;
        
        var topLevel = TopLevel.GetTopLevel(storageProvider.MainWindow);
        if (topLevel?.StorageProvider == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation
        });

        return folders;
    }
    
    public static void OpenFolderInFileManager(string folderPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start("explorer.exe", folderPath);
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", folderPath);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", folderPath);
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported operating system.");
        }
    }
}

