using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features;

public class MiiExtensionsTests
{
    [Fact]
    public void GetCreationDateUtc_ShouldDecodeLower29Bits()
    {
        const uint counter = 12345;
        var regularMiiId = (0b100u << 29) | counter;
        var expected = new DateTime(2006, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(counter * 4d);

        var creationDate = MiiExtensions.GetCreationDateUtc(regularMiiId);

        Assert.Equal(expected, creationDate);
    }

    [Fact]
    public void GetCreationDateUtc_ShouldIgnorePrefixBits()
    {
        const uint counter = 98765;
        var regularMiiId = (0b100u << 29) | counter;
        var blueMiiId = (0b110u << 29) | counter;

        var regularCreationDate = MiiExtensions.GetCreationDateUtc(regularMiiId);
        var blueCreationDate = MiiExtensions.GetCreationDateUtc(blueMiiId);

        Assert.Equal(regularCreationDate, blueCreationDate);
    }

    [Fact]
    public void TryGetCreationDateUtc_ShouldReturnFalse_WhenMiiIdIsZero()
    {
        var mii = new Mii { MiiId = 0 };

        var hasCreationDate = mii.TryGetCreationDateUtc(out var creationDateUtc);

        Assert.False(hasCreationDate);
        Assert.Equal(default, creationDateUtc);
    }
}
