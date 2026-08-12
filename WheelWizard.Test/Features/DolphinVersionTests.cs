using WheelWizard.DolphinInstaller;

namespace WheelWizard.Test.Features;

public class DolphinVersionTests
{
    [Theory]
    // The two patched builds, and anything past them.
    [InlineData("2606a")]
    [InlineData("2606b")]
    [InlineData("2606-235")]
    [InlineData("2606-274")]
    [InlineData("2607")]
    [InlineData("2701a")]
    // The shapes we get from the actual probes rather than from a bare setting.
    [InlineData("Dolphin 2606-274")]
    [InlineData("  2606a\n")]
    public void GetStatus_AcceptsThePatchedBuilds(string text)
    {
        Assert.Equal(DolphinVersionStatus.Supported, DolphinVersion.GetStatus(text));
    }

    [Theory]
    // The initial 2606 release predates the point release that carries the fix.
    [InlineData("2606")]
    [InlineData("2606-234")]
    [InlineData("2603a")]
    [InlineData("2503")]
    [InlineData("2412")]
    // What the official 2606 release reports itself as.
    [InlineData("Dolphin 2606")]
    // The old scheme is always older, even though its build numbers are much larger.
    // Notably "5.0-21460" must not be mined for a four-digit "2146".
    [InlineData("5.0-21460")]
    [InlineData("5.0")]
    [InlineData("Dolphin 4.0.2")]
    public void GetStatus_RejectsEverythingOlderThanTheFix(string text)
    {
        Assert.Equal(DolphinVersionStatus.Outdated, DolphinVersion.GetStatus(text));
    }

    [Theory]
    // Forks and unreadable probes fail open: we warn about what we know, not about what we guessed.
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ishiiruka")]
    [InlineData("some-custom-build")]
    // Four-digit numbers that are not a plausible year-month from the YYMM era.
    [InlineData("2411")]
    [InlineData("2613")]
    [InlineData("2600")]
    public void GetStatus_FailsOpenWhenTheVersionIsUnreadable(string? text)
    {
        Assert.Equal(DolphinVersionStatus.Unknown, DolphinVersion.GetStatus(text));
    }

    [Theory]
    // What the popup shows: the version as found in the probe text.
    [InlineData("Dolphin Emulator 2606-100", "2606-100")]
    [InlineData("Dolphin 5.0-21460", "5.0-21460")]
    public void GetStatus_ReturnsTheVersionAsFoundInTheText(string text, string expectedFound)
    {
        DolphinVersion.GetStatus(text, out var foundVersion);

        Assert.Equal(expectedFound, foundVersion);
    }
}
