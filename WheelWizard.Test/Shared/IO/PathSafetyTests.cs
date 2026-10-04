using WheelWizard.Shared.IO;

namespace WheelWizard.Test.Shared.IO;

public class PathSafetyTests
{
    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("nested/../../outside.bin")]
    [InlineData("nested\\..\\outside.bin")]
    [InlineData("/absolute.bin")]
    [InlineData("C:\\absolute.bin")]
    [InlineData(" ")]
    public void UnsafeRelativePathsAreRejected(string path)
    {
        Assert.False(PathSafety.TryGetPathWithinDirectory(Path.GetTempPath(), path, out var fullPath));
        Assert.Empty(fullPath);
    }

    [Fact]
    public void NestedPathIsNormalizedInsideDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "PathSafetyTests");
        Assert.True(PathSafety.TryGetPathWithinDirectory(root, " ./nested\\file.bin ", out var path));
        Assert.Equal(Path.Combine(root, "nested", "file.bin"), path);
        Assert.False(PathSafety.IsPathWithinDirectory(root, root + "-sibling/file.bin"));
        Assert.True(PathSafety.IsPathWithinDirectory(root, root));
    }

    [Theory]
    [InlineData("folder\\mii.bin", "mii.bin")]
    [InlineData("folder/mii.bin", "mii.bin")]
    [InlineData("\"mii.bin\"", "mii.bin")]
    public void SafeFileNameReturnsOnlyLeaf(string input, string expected)
    {
        Assert.True(PathSafety.TryGetSafeFileName(input, out var result));
        Assert.Equal(expected, result);
    }
}
