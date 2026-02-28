using Testably.Abstractions.Testing;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.Test.Features.MiiRendering;

public class MiiRenderingResourceLocatorTests
{
    [Fact]
    public void GetFflResourcePath_ShouldResolveFromConfiguredDirectory()
    {
        var fileSystem = new MockFileSystem();
        var directoryPath = "/ffl";
        var filePath = "/ffl/FFLResHigh.dat";

        fileSystem.Directory.CreateDirectory(directoryPath);
        fileSystem.File.WriteAllBytes(filePath, new byte[2 * 1024 * 1024]);

        var configuration = new MiiRenderingConfiguration { ResourcePath = directoryPath, MinimumExpectedSizeBytes = 16 };

        var locator = new MiiRenderingResourceLocator(fileSystem, configuration);
        var result = locator.GetFflResourcePath();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(fileSystem.Path.GetFullPath(filePath), result.Value);
    }

    [Fact]
    public void GetFflResourcePath_ShouldFailWhenMissing()
    {
        var fileSystem = new MockFileSystem();
        var configuration = new MiiRenderingConfiguration
        {
            ResourcePath = "/missing",
            AdditionalSearchDirectories = [],
            MinimumExpectedSizeBytes = 16,
        };

        var locator = new MiiRenderingResourceLocator(fileSystem, configuration);
        var result = locator.GetFflResourcePath();

        Assert.True(result.IsFailure);
        Assert.Contains("FFLResHigh.dat", result.Error!.Message);
    }

    [Fact]
    public void GetFflResourcePath_ShouldFailWhenFileTooSmall()
    {
        var fileSystem = new MockFileSystem();
        var filePath = "/ffl/FFLResHigh.dat";

        fileSystem.Directory.CreateDirectory("/ffl");
        fileSystem.File.WriteAllBytes(filePath, [0x00, 0x01, 0x02]);

        var configuration = new MiiRenderingConfiguration { ResourcePath = filePath, MinimumExpectedSizeBytes = 64 };

        var locator = new MiiRenderingResourceLocator(fileSystem, configuration);
        var result = locator.GetFflResourcePath();

        Assert.True(result.IsFailure);
        Assert.Contains("invalid or incomplete", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFflResourcePath_ShouldResolveFromAncestorDirectoryOfCurrentWorkingDirectory()
    {
        var fileSystem = new MockFileSystem();
        var currentDirectory = fileSystem.Path.GetFullPath(Environment.CurrentDirectory);
        var parentDirectory = fileSystem.DirectoryInfo.New(currentDirectory).Parent?.FullName;
        Assert.False(string.IsNullOrWhiteSpace(parentDirectory));

        var filePath = fileSystem.Path.Combine(parentDirectory!, "FFLResHigh.dat");
        fileSystem.Directory.CreateDirectory(parentDirectory!);
        fileSystem.File.WriteAllBytes(filePath, new byte[2 * 1024 * 1024]);

        var configuration = new MiiRenderingConfiguration { MinimumExpectedSizeBytes = 16 };
        var locator = new MiiRenderingResourceLocator(fileSystem, configuration);

        var result = locator.GetFflResourcePath();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(fileSystem.Path.GetFullPath(filePath), result.Value);
    }
}
