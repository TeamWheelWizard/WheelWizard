using System.Diagnostics;
using Avalonia.Platform.Storage;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Views.Storage;

public sealed class FilePickerService(IStorageProviderAccessor storage, IRuntimeEnvironment environment, IProcessLauncher processes)
    : IFilePickerService
{
    /// <summary>
    /// Opens a file picker with the specified options.
    /// </summary>
    /// <param name="fileType">The file type filter to use.</param>
    /// <param name="allowMultiple">Whether multiple file selection is allowed.</param>
    /// <param name="title">The title of the file picker dialog.</param>
    /// <returns>A list of selected file paths or an empty list if no files were selected.</returns>
    public async Task<List<string>> OpenFilePickerAsync(
        FilePickerFileType fileType,
        bool allowMultiple = true,
        string title = "Select Files"
    )
    {
        var provider = storage.Current;
        if (provider == null)
            return [];

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = new List<FilePickerFileType> { fileType },
        };

        var selectedFiles = await provider.OpenFilePickerAsync(options);

        return selectedFiles
                ?.Select(StoragePaths.TryResolveLocalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToList() ?? [];
    }

    public async Task<string?> OpenSingleFileAsync(string title, IEnumerable<FilePickerFileType> fileTypes)
    {
        var provider = storage.Current;
        if (provider == null)
            return null;

        var files = await provider.OpenFilePickerAsync(
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
            var path = StoragePaths.TryResolveLocalPath(file);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        return null;
    }

    public async Task<IReadOnlyList<IStorageFolder?>> SelectFolderAsync(string title, IStorageFolder? suggestedStartLocation = null)
    {
        var provider = storage.Current;
        if (provider == null)
            return [];

        var folders = await provider.OpenFolderPickerAsync(
            new()
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStartLocation,
            }
        );

        return folders;
    }

    public void OpenFolderInFileManager(string folderPath)
    {
        string? openExecutable;
        if (environment.IsWindows)
        {
            openExecutable = "explorer.exe";
        }
        else if (environment.IsLinux)
        {
            openExecutable = "xdg-open";
        }
        else if (environment.IsMacOS)
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

        processes.Start(info);
    }

    public async Task<string?> SaveFileAsync(
        string title,
        IEnumerable<FilePickerFileType> fileTypes,
        string defaultFileName = "untitled",
        IStorageFolder? suggestedStartLocation = null
    )
    {
        var provider = storage.Current;
        if (provider == null)
            return null;

        var file = await provider.SaveFilePickerAsync(
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

        return StoragePaths.TryResolveLocalPath(file);
    }
}
