﻿using System.Diagnostics;
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

        return selectedFiles?.Select(file => file.Path.LocalPath).ToList() ?? [];
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

        return files?.FirstOrDefault()?.Path.LocalPath;
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

        return files?.Select(file => file.Path.LocalPath).ToList() ?? [];
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

        return file?.Path.LocalPath;
    }
}
