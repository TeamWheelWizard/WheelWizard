using WheelWizard.Features.Patches;

namespace WheelWizard.Test.Features.Patches;

public class LooseBrsarPatchFileNameTests
{
    [Theory]
    [InlineData("[001]sound.BRWSD", "1.brwsd")]
    [InlineData("[42].BRBNK", "42.brbnk")]
    [InlineData("[0]name.brwsd", "0.brwsd")]
    public void TaggedNamesNormalizeIdAndExtension(string name, string expected)
    {
        Assert.True(LooseBrsarPatchFileName.TryGetNormalizedFileName(name, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]sound.brwsd")]
    [InlineData("[-1]sound.brwsd")]
    [InlineData("[2147483648]sound.brwsd")]
    [InlineData("[1]sound.szs")]
    [InlineData("1.brwsd")]
    public void InvalidNamesAreRejected(string name)
    {
        Assert.False(LooseBrsarPatchFileName.TryGetNormalizedFileName(name, out var result));
        Assert.Empty(result);
    }
}
