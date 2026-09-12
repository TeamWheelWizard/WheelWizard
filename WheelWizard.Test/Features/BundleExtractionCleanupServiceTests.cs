using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Testably.Abstractions.Testing;
using WheelWizard.AutoUpdating;

namespace WheelWizard.Test.Features;

public class BundleExtractionCleanupServiceTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly BundleExtractionCleanupService _service;

    private readonly string _extractionRoot;
    private readonly string _appDirectory;
    private readonly string _currentExtraction;

    public BundleExtractionCleanupServiceTests()
    {
        _service = new(_fileSystem, Substitute.For<ILogger<BundleExtractionCleanupService>>());

        _extractionRoot = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/", "temp", ".net"));
        _appDirectory = _fileSystem.Path.Combine(_extractionRoot, "WheelWizard");
        _currentExtraction = _fileSystem.Path.Combine(_appDirectory, "current-bundle-id");
    }

    private string CreateExtraction(string appName, string bundleId)
    {
        var directory = _fileSystem.Path.Combine(_extractionRoot, appName, bundleId);
        _fileSystem.Directory.CreateDirectory(directory);
        _fileSystem.File.WriteAllText(_fileSystem.Path.Combine(directory, "libSomething.dll"), "native");
        return directory;
    }

    private static string JoinSearchDirectories(params string[] directories) => string.Join(Path.PathSeparator, directories);

    [Fact]
    public void CleanupStaleExtractions_RemovesOtherVersions_ButKeepsCurrentOne()
    {
        CreateExtraction("WheelWizard", "current-bundle-id");
        var oldA = CreateExtraction("WheelWizard", "old-bundle-a");
        var oldB = CreateExtraction("WheelWizard", "old-bundle-b");

        var removed = _service.CleanupStaleExtractions(_currentExtraction);

        Assert.Equal(2, removed);
        Assert.True(_fileSystem.Directory.Exists(_currentExtraction));
        Assert.True(_fileSystem.File.Exists(_fileSystem.Path.Combine(_currentExtraction, "libSomething.dll")));
        Assert.False(_fileSystem.Directory.Exists(oldA));
        Assert.False(_fileSystem.Directory.Exists(oldB));
    }

    [Fact]
    public void CleanupStaleExtractions_RemovesUpdateExecutableExtractions()
    {
        CreateExtraction("WheelWizard", "current-bundle-id");
        CreateExtraction("WheelWizard_new", "downloaded-bundle-a");
        CreateExtraction("WheelWizard_new", "downloaded-bundle-b");
        var updateAppDirectory = _fileSystem.Path.Combine(_extractionRoot, "WheelWizard_new");

        var removed = _service.CleanupStaleExtractions(_currentExtraction);

        Assert.Equal(1, removed);
        Assert.False(_fileSystem.Directory.Exists(updateAppDirectory));
        Assert.True(_fileSystem.Directory.Exists(_currentExtraction));
    }

    [Fact]
    public void CleanupStaleExtractions_LeavesOtherApplicationsAlone()
    {
        CreateExtraction("WheelWizard", "current-bundle-id");
        var otherApp = CreateExtraction("SomeOtherApp", "some-bundle-id");
        var similarName = CreateExtraction("WheelWizardTool", "some-bundle-id");

        var removed = _service.CleanupStaleExtractions(_currentExtraction);

        Assert.Equal(0, removed);
        Assert.True(_fileSystem.Directory.Exists(otherApp));
        Assert.True(_fileSystem.Directory.Exists(similarName));
    }

    [Fact]
    public void CleanupStaleExtractions_ReturnsZero_WhenNothingExists()
    {
        var removed = _service.CleanupStaleExtractions(_currentExtraction);

        Assert.Equal(0, removed);
    }

    [Fact]
    public void CleanupStaleExtractions_AcceptsTrailingSeparatorForCurrentDirectory()
    {
        CreateExtraction("WheelWizard", "current-bundle-id");
        var old = CreateExtraction("WheelWizard", "old-bundle");

        var removed = _service.CleanupStaleExtractions(_currentExtraction + _fileSystem.Path.DirectorySeparatorChar);

        Assert.Equal(1, removed);
        Assert.True(_fileSystem.Directory.Exists(_currentExtraction));
        Assert.False(_fileSystem.Directory.Exists(old));
    }

    [Fact]
    public void ResolveExtractionDirectory_FindsExtractionDirectory_FromNativeSearchDirectories()
    {
        var executableDirectory = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/", "apps", "ww"));
        var processPath = _fileSystem.Path.Combine(executableDirectory, "WheelWizard.exe");
        var searchDirectories = JoinSearchDirectories(_currentExtraction + _fileSystem.Path.DirectorySeparatorChar, executableDirectory);

        var resolved = _service.ResolveExtractionDirectory(processPath, searchDirectories);

        Assert.Equal(_currentExtraction, resolved);
    }

    [Fact]
    public void ResolveExtractionDirectory_ReturnsNull_WhenNotRunningFromBundle()
    {
        var executableDirectory = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/", "apps", "ww"));
        var processPath = _fileSystem.Path.Combine(executableDirectory, "WheelWizard.exe");

        Assert.Null(_service.ResolveExtractionDirectory(processPath, executableDirectory));
        Assert.Null(_service.ResolveExtractionDirectory(processPath, null));
        Assert.Null(_service.ResolveExtractionDirectory(null, _currentExtraction));
    }

    [Fact]
    public void ResolveExtractionDirectory_IgnoresExecutableDirectory_EvenWhenParentMatchesAppName()
    {
        // e.g. C:\WheelWizard\WheelWizard\WheelWizard.exe must never be treated as an extraction folder
        var executableDirectory = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/", "WheelWizard", "WheelWizard"));
        var processPath = _fileSystem.Path.Combine(executableDirectory, "WheelWizard.exe");

        var resolved = _service.ResolveExtractionDirectory(processPath, executableDirectory);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveExtractionDirectory_ReturnsNull_WhenLayoutIsNotUnderDotNetFolder()
    {
        var executableDirectory = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/", "apps", "ww"));
        var processPath = _fileSystem.Path.Combine(executableDirectory, "WheelWizard.exe");
        var notAnExtraction = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/", "cache", "WheelWizard", "some-id"));

        var resolved = _service.ResolveExtractionDirectory(processPath, JoinSearchDirectories(notAnExtraction, executableDirectory));

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveExtractionDirectory_ReturnsNull_WhenAppNameDoesNotMatch()
    {
        var executableDirectory = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("/", "apps", "ww"));
        var processPath = _fileSystem.Path.Combine(executableDirectory, "WheelWizard.exe");
        var otherApp = _fileSystem.Path.Combine(_extractionRoot, "SomeOtherApp", "some-id");

        var resolved = _service.ResolveExtractionDirectory(processPath, JoinSearchDirectories(otherApp, executableDirectory));

        Assert.Null(resolved);
    }
}
