using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.MiiAnimations.Library;

namespace WheelWizard.Test.Features.MiiAnimations;

public class MiiAnimationLibraryTests
{
    [Fact]
    public void EveryBundledAnimation_Loads()
    {
        var library = new MiiAnimationLibrary(NullLogger<MiiAnimationLibrary>.Instance);
        var paths = typeof(MiiAnimationLibrary)
            .Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("MiiAnimations/", StringComparison.Ordinal))
            .Select(name => name["MiiAnimations/".Length..])
            .ToList();

        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.NotNull(library.Get(path)));
    }
}
