using System.Net;
using System.Net.Http;
using System.Text;
using WheelWizard.Services;

namespace WheelWizard.Test.Features.Ghosts;

public class GhostTrackServiceTests
{
    [Fact]
    public async Task GetAllTracksAsync_LoadsTracksWithoutEagerHashResolution_AndCachesResults()
    {
        var handler = new StubHttpMessageHandler(new Dictionary<string, string>
        {
            ["https://rwfc.net/api/timetrial/tracks"] =
                """
                [
                  { "id": 1, "name": "Main Track", "courseId": 300, "category": "retro", "laps": 3, "supportsGlitch": true, "sortOrder": 1 },
                  { "id": 2, "name": "Variant Track", "courseId": 300, "category": "retro", "laps": 3, "supportsGlitch": true, "sortOrder": 2 }
                ]
                """,
            ["https://rwfc.net/api/timetrial/worldrecords/all?glitchAllowed=true&cc=150"] =
                """
                [
                  { "trackId": 1, "trackName": "Main Track", "activeWorldRecord": { "id": 100, "trackId": 1, "trackName": "Main Track", "playerName": "P1", "cc": 150, "finishTimeMs": 100000, "finishTimeDisplay": "1:40.000", "miiName": "Mii", "dateSet": "2026-01-01", "submittedAt": "2026-01-01T00:00:00Z", "rank": 1 } },
                  { "trackId": 2, "trackName": "Variant Track", "activeWorldRecord": { "id": 101, "trackId": 2, "trackName": "Variant Track", "playerName": "P2", "cc": 150, "finishTimeMs": 101000, "finishTimeDisplay": "1:41.000", "miiName": "Mii", "dateSet": "2026-01-01", "submittedAt": "2026-01-01T00:00:00Z", "rank": 1 } }
                ]
                """
        });

        using var client = new HttpClient(handler);
        var hexMapping = new TrackHexMappingService();
        var variantMapping = new TrackVariantMappingService();
        var service = new GhostTrackService(client, hexMapping, variantMapping);

        var firstLoad = await service.GetAllTracksAsync();
        var secondLoad = await service.GetAllTracksAsync();

        Assert.Equal(2, firstLoad.Count);
        Assert.All(firstLoad, t => Assert.Equal(string.Empty, t.HexValue));
        Assert.Equal(firstLoad.Count, secondLoad.Count);

        Assert.Equal(1, handler.GetRequestCount("https://rwfc.net/api/timetrial/tracks"));
        Assert.Equal(1, handler.GetRequestCount("https://rwfc.net/api/timetrial/worldrecords/all?glitchAllowed=true&cc=150"));
    }

    [Fact]
    public async Task EnsureTrackMappingsInitializedAsync_InitializesVariantMappingAfterTrackLoad()
    {
        var handler = new StubHttpMessageHandler(new Dictionary<string, string>
        {
            ["https://rwfc.net/api/timetrial/tracks"] =
                """
                [
                  { "id": 10, "name": "Main Track", "courseId": 410, "category": "retro", "laps": 3, "supportsGlitch": true, "sortOrder": 1 },
                  { "id": 11, "name": "Variant Track", "courseId": 410, "category": "retro", "laps": 3, "supportsGlitch": true, "sortOrder": 2 }
                ]
                """,
            ["https://rwfc.net/api/timetrial/worldrecords/all?glitchAllowed=true&cc=150"] = "[]"
        });

        using var client = new HttpClient(handler);
        var hexMapping = new TrackHexMappingService();
        var variantMapping = new TrackVariantMappingService();
        var service = new GhostTrackService(client, hexMapping, variantMapping);

        await service.GetAllTracksAsync();
        Assert.False(variantMapping.IsVariantTrack("Variant Track"));

        await service.EnsureTrackMappingsInitializedAsync();

        Assert.True(variantMapping.IsVariantTrack("Variant Track"));
        Assert.Equal("Main Track", variantMapping.GetMainTrackName("Variant Track"));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;
        private readonly Dictionary<string, int> _requestCounts = new(StringComparer.OrdinalIgnoreCase);

        public StubHttpMessageHandler(Dictionary<string, string> responses)
        {
            _responses = responses;
        }

        public int GetRequestCount(string url)
        {
            return _requestCounts.TryGetValue(url, out var count) ? count : 0;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            _requestCounts[url] = GetRequestCount(url) + 1;

            if (!_responses.TryGetValue(url, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
