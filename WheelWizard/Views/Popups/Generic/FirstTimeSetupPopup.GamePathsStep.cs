using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using WheelWizard.Services;

namespace WheelWizard.Views.Popups.Generic;

public partial class FirstTimeSetupPopup
{
    private string? _selectedUserFolder;
    private string? _selectedGameFile;

    private bool IsGamePathsStepComplete =>
        !string.IsNullOrWhiteSpace(_selectedUserFolder) && !string.IsNullOrWhiteSpace(_selectedGameFile);

    private void SaveGamePathsStep()
    {
        SettingsService.Set(SettingsService.USER_FOLDER_PATH, _selectedUserFolder!);
        SettingsService.Set(SettingsService.GAME_LOCATION, _selectedGameFile!);
    }

    private void AutoDetectPaths()
    {
        var folderPath = PathManager.TryFindUserFolderPath();
        if (!string.IsNullOrEmpty(folderPath))
            UserFolderTextBox.Text = folderPath;
        _selectedUserFolder = folderPath;
    }

    private async void BrowseUserFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await FilePickerHelper.SelectFolderAsync("Select the Dolphin user folder", owner: this);
            var path = FilePickerHelper.TryResolveLocalPath(folders.FirstOrDefault());
            if (string.IsNullOrWhiteSpace(path))
                return;

            UserFolderTextBox.Text = path;
            _selectedUserFolder = path;
            UpdateContinueState();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to pick the Dolphin user folder.");
            ShowError("Something went wrong while selecting the folder.");
        }
    }

    private async void BrowseGameFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var gameFileType = new FilePickerFileType("Wii game files")
            {
                Patterns = ["*.iso", "*.wbfs", "*.rvz", "*.ciso", "*.nkit.iso", "*.wia"],
            };
            var path = await FilePickerHelper.OpenSingleFileAsync("Select your Mario Kart Wii game file", [gameFileType], this);
            if (string.IsNullOrWhiteSpace(path))
                return;

            GameFileTextBox.Text = path;
            _selectedGameFile = path;
            UpdateContinueState();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to pick the Mario Kart Wii game file.");
            ShowError("Something went wrong while selecting the file.");
        }
    }
}
