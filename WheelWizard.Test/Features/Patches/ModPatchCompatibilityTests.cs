using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.Features.Patches;
using WheelWizard.Models.Mods;
using WheelWizard.Mods;

namespace WheelWizard.Test.Features.Patches;

public class ModPatchCompatibilityTests
{
    [Theory]
    [InlineData("course.szs", true)]
    [InlineData("COURSE.SZS", true)]
    [InlineData("nested/course.szs", true)]
    [InlineData("revo_kart.brsar", true)]
    [InlineData("[42]sound.BRWSD", true)]
    [InlineData("course.tag.szs", false)]
    [InlineData("notes.txt", false)]
    [InlineData("sound.brwsd", false)]
    public void CompatibilityUsesInjectedFilesAndRecognizesConvertibleArchives(string relativePath, bool expected)
    {
        var fs = new MockFileSystem();
        var root = fs.Path.GetFullPath("mods/Test");
        var file = fs.Path.GetFullPath(fs.Path.Combine(root, relativePath));
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(file)!);
        fs.File.WriteAllText(file, "archive");
        var paths = Substitute.For<IModPaths>();
        paths.GetModDirectoryPath("Test").Returns(root);
        var service = new ModPatchConversionService(
            Substitute.For<ISzsPatchConverter>(),
            NullLogger<ModPatchConversionService>.Instance,
            fs,
            paths,
            Substitute.For<IGameBaselineStore>()
        );
        var mod = new Mod { Title = "Test" };

        service.RefreshCompatibility(mod);

        Assert.Equal(expected, mod.HasIncompatibleFiles);
        Assert.Equal(expected ? new[] { file } : [], service.GetConvertibleArchiveFiles(mod));
        fs.Directory.Delete(root, recursive: true);
        service.RefreshCompatibility(mod);
        Assert.False(mod.HasIncompatibleFiles);
        Assert.Empty(service.GetConvertibleArchiveFiles(mod));
    }
}
