using Testably.Abstractions.Testing;
using WheelWizard.Shared.IO;

namespace WheelWizard.Test.Shared.IO;

public class PathExtensionsTests
{
    [Theory]
    [InlineData(SimulationMode.Windows, "C:\\", "C:\\")]
    [InlineData(SimulationMode.Linux, "/", "/")]
    [InlineData(SimulationMode.Windows, "C:\\folder\\", "C:\\folder")]
    [InlineData(SimulationMode.Linux, "/folder/", "/folder")]
    public void NormalizePath_PreservesRootsAndTrimsDirectorySeparators(SimulationMode platform, string input, string expected)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(platform));

        Assert.Equal(expected, fs.Path.NormalizePath(input));
    }

    [Theory]
    [InlineData(SimulationMode.Windows, true)]
    [InlineData(SimulationMode.Linux, false)]
    public void Equality_UsesFilesystemPlatformCaseRules(SimulationMode platform, bool expectedEqual)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(platform));

        Assert.Equal(expectedEqual, fs.Path.PathsEqual("/folder", "/FOLDER"));
    }

    [Theory]
    [InlineData("/data/child", "/data", true)]
    [InlineData("/data/..cache", "/data", true)]
    [InlineData("/data/..cache/nested", "/data", true)]
    [InlineData("/data/../data/..cache", "/data", true)]
    [InlineData("/data/..", "/data", false)]
    [InlineData("/data-other", "/data", false)]
    [InlineData("/data", "/data", false)]
    [InlineData("/data/../other", "/data", false)]
    public void DescendantCheck_RespectsDirectoryBoundaries(string descendant, string ancestor, bool expected)
    {
        var fs = new MockFileSystem();

        Assert.Equal(expected, fs.Path.IsDescendantPath(descendant, ancestor));
    }

    [Theory]
    [InlineData(SimulationMode.Windows)]
    [InlineData(SimulationMode.Linux)]
    public void DescendantCheck_DistinguishesDotPrefixedNamesFromParentSegments(SimulationMode platform)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(platform));
        var ancestor = fs.Path.GetFullPath("/data");

        Assert.True(fs.Path.IsDescendantPath(fs.Path.Combine(ancestor, "..cache"), ancestor));
        Assert.False(fs.Path.IsDescendantPath(fs.Path.Combine(ancestor, ".."), ancestor));
        Assert.False(fs.Path.IsDescendantPath(fs.Path.Combine(ancestor, "..", "other"), ancestor));
    }
}
