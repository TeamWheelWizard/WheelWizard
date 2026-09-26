using System.IO.Abstractions;
using WheelWizard.ApplicationData;

namespace WheelWizard.Mods;

public interface IModPaths
{
    string RootFolderPath { get; }
    string GetModDirectoryPath(string name);
}

public sealed class ModPaths(IApplicationDataLocation applicationData, IFileSystem fileSystem) : IModPaths
{
    public string RootFolderPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "Mods");

    public string GetModDirectoryPath(string name) => fileSystem.Path.Combine(RootFolderPath, name);
}
