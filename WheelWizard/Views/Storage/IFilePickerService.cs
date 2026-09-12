using Avalonia.Platform.Storage;

namespace WheelWizard.Views.Storage;

public interface IFilePickerService
{
    Task<List<string>> OpenFilePickerAsync(FilePickerFileType fileType, bool allowMultiple = true, string title = "Select Files");
    Task<string?> OpenSingleFileAsync(string title, IEnumerable<FilePickerFileType> fileTypes);
    Task<IReadOnlyList<IStorageFolder?>> SelectFolderAsync(string title, IStorageFolder? suggestedStartLocation = null);
    void OpenFolderInFileManager(string folderPath);
    Task<string?> SaveFileAsync(
        string title,
        IEnumerable<FilePickerFileType> fileTypes,
        string defaultFileName = "untitled",
        IStorageFolder? suggestedStartLocation = null
    );
}
