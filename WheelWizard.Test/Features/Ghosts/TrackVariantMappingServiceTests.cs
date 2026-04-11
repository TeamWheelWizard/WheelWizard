using WheelWizard.Models;
using WheelWizard.Services;

namespace WheelWizard.Test.Features.Ghosts;

public class TrackVariantMappingServiceTests
{
    [Fact]
    public void InitializeFromTrackInfo_BuildsVariantAndMainMappingsByCourseId()
    {
        var service = new TrackVariantMappingService();

        var tracks = new[]
        {
            new TrackInfo { Id = 10, Name = "Main Track", CourseId = 300 },
            new TrackInfo { Id = 11, Name = "Variant Track", CourseId = 300 },
            new TrackInfo { Id = 20, Name = "Solo Track", CourseId = 301 }
        };

        service.InitializeFromTrackInfo(tracks);

        Assert.True(service.IsVariantTrack("Variant Track"));
        Assert.False(service.IsVariantTrack("Main Track"));
        Assert.Equal("Main Track", service.GetMainTrackName("Variant Track"));
        Assert.Equal("Solo Track", service.GetMainTrackName("Solo Track"));

        var variants = service.GetVariantsForMainTrack("Main Track");
        Assert.Single(variants);
        Assert.Contains("Variant Track", variants);
        Assert.Empty(service.GetVariantsForMainTrack("Solo Track"));
    }

    [Fact]
    public void AddAndRemoveVariantMapping_UpdatesVariantState()
    {
        var service = new TrackVariantMappingService();

        service.AddVariantMapping("Main A", "Variant A1");

        Assert.True(service.IsVariantTrack("Variant A1"));
        Assert.Equal("Main A", service.GetMainTrackName("Variant A1"));
        Assert.Contains("Variant A1", service.GetVariantsForMainTrack("Main A"));

        service.RemoveVariantMapping("Variant A1");

        Assert.False(service.IsVariantTrack("Variant A1"));
        Assert.Equal("Variant A1", service.GetMainTrackName("Variant A1"));
        Assert.Empty(service.GetVariantsForMainTrack("Main A"));
    }
}
