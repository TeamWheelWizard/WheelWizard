using System.IO.Abstractions;
using WheelWizard.ApplicationData;

namespace WheelWizard.MiiRendering.Configuration;

public interface IMiiRenderingPaths
{
    string ManagedResourcePath { get; }
}

public sealed class MiiRenderingPaths(IApplicationDataLocation applicationData, IFileSystem fileSystem) : IMiiRenderingPaths
{
    public string ManagedResourcePath =>
        fileSystem.Path.Combine(applicationData.DirectoryPath, "MiiRendering", MiiRenderingConfiguration.ResourceFileName);
}
