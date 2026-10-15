using System.IO.Abstractions;
using WheelWizard.Settings;

namespace WheelWizard.Dolphin.Paths;

public interface IDolphinPaths
{
    DolphinPathLayout Layout { get; }
    string ExecutablePath { get; }
    string UserFolderPath { get; }
    string ConfigFolderPath { get; }
    bool HasCustomLoadFolder { get; }

    /// <summary>Dolphin's Load folder setting as written, even when that folder is not there right now (an unplugged drive).</summary>
    string ConfiguredLoadFolderPath { get; }

    /// <summary>The user folder setting as written, even when that folder is not there right now (an unplugged drive).</summary>
    string ConfiguredUserFolderPath { get; }

    string LoadFolderPath { get; }
    string WiiFolderPath { get; }
}

public sealed class DolphinPaths(ISettingsManager settings, IDolphinPathResolver resolver, IFileSystem fileSystem) : IDolphinPaths
{
    public DolphinPathLayout Layout => resolver.Resolve(settings.Get<string>(settings.DOLPHIN_LOCATION), UserFolderPath);
    public string ExecutablePath => Layout.DolphinFilePath;
    public string UserFolderPath => settings.Get<string>(settings.USER_FOLDER_PATH);
    public string ConfigFolderPath => Layout.ConfigFolderPath;
    public bool HasCustomLoadFolder => settings.LOAD_PATH.IsValid();
    public string ConfiguredLoadFolderPath =>
        settings.Get<string>(settings.LOAD_PATH) is { Length: > 0 } value ? value : settings.LOAD_PATH.UnavailableValue ?? "";

    public string ConfiguredUserFolderPath =>
        UserFolderPath is { Length: > 0 } value ? value : settings.USER_FOLDER_PATH.UnavailableValue ?? "";
    public string LoadFolderPath =>
        HasCustomLoadFolder ? settings.Get<string>(settings.LOAD_PATH) : fileSystem.Path.Combine(UserFolderPath, "Load");
    public string WiiFolderPath =>
        settings.NAND_ROOT_PATH.IsValid() ? settings.Get<string>(settings.NAND_ROOT_PATH) : fileSystem.Path.Combine(UserFolderPath, "Wii");
}
