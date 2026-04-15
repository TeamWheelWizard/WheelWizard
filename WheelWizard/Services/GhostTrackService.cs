using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using WheelWizard.Models;
using Serilog;

namespace WheelWizard.Services
{
    public class GhostTrackService
    {
        private readonly HttpClient _httpClient;
        private readonly TrackHexMappingService _hexMappingService;
        private readonly TrackVariantMappingService _variantMappingService;
        private List<ApiTrack> _allTracks = new();
        private Dictionary<int, TrackInfo> _trackInfoCache = new();
        private bool _tracksLoaded = false;
        private bool _trackInfoLoaded = false;
        private bool _trackMappingsInitialized = false;

        public GhostTrackService(
            HttpClient httpClient,
            TrackHexMappingService hexMappingService,
            TrackVariantMappingService variantMappingService)
        {
            _httpClient = httpClient;
            _hexMappingService = hexMappingService;
            _variantMappingService = variantMappingService;
        }

        public async Task<List<ApiTrack>> GetAllTracksAsync()
        {
            if (_tracksLoaded && _allTracks.Count > 0)
            {
                Log.Debug("Returning cached track data ({Count} tracks)", _allTracks.Count);
                return _allTracks;
            }

            try
            {
                Log.Information("Loading tracks from API (first time)");

                var trackInfoTask = LoadTrackInfoAsync(initializeMappings: false);
                const string worldRecordsUrl = "https://rwfc.net/api/timetrial/worldrecords/all?glitchAllowed=true&cc=150";
                var worldRecordsTask = _httpClient.GetStringAsync(worldRecordsUrl);

                await Task.WhenAll(trackInfoTask, worldRecordsTask).ConfigureAwait(false);

                var response = await worldRecordsTask.ConfigureAwait(false);

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var worldRecords = await Task.Run(() =>
                    JsonSerializer.Deserialize<WorldRecordsResponse[]>(response, jsonOptions)
                ).ConfigureAwait(false);

                if (worldRecords != null)
                {
                    _allTracks = await Task.Run(() =>
                        worldRecords
                            .Where(wr => wr.TrackId > 0)
                            .Select(wr =>
                            {
                                _trackInfoCache.TryGetValue(wr.TrackId, out var trackInfo);
                                var track = ApiTrack.FromWorldRecordAndTrackInfo(wr, trackInfo);
                                track.HexValue = string.Empty;

                                return track;
                            })
                            .OrderBy(t => t.TrackType)
                            .ThenBy(t => t.Name)
                            .ToList()
                    ).ConfigureAwait(false);

                    _tracksLoaded = true;
                    Log.Information("Loaded and cached {Count} tracks from API with category data", _allTracks.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load tracks from API");
            }

            return _allTracks;
        }

        private async Task LoadTrackInfoAsync(bool initializeMappings = true)
        {
            if (_trackInfoLoaded && _trackInfoCache.Count > 0)
            {
                Log.Debug("Using cached track info ({Count} tracks)", _trackInfoCache.Count);
                if (initializeMappings && !_trackMappingsInitialized)
                {
                    var tracks = _trackInfoCache.Values.ToArray();
                    _hexMappingService.InitializeFromTrackInfo(tracks);
                    _variantMappingService.InitializeFromTrackInfo(tracks);
                    _trackMappingsInitialized = true;
                }
                return;
            }

            try
            {
                Log.Information("Loading track info from tracks API (first time)");
                const string tracksUrl = "https://rwfc.net/api/timetrial/tracks";
                var response = await _httpClient.GetStringAsync(tracksUrl).ConfigureAwait(false);

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var tracks = await Task.Run(() =>
                    JsonSerializer.Deserialize<TrackInfo[]>(response, jsonOptions)
                ).ConfigureAwait(false);

                if (tracks != null)
                {
                    _trackInfoCache = tracks.ToDictionary(t => t.Id);
                    if (initializeMappings)
                    {
                        _hexMappingService.InitializeFromTrackInfo(tracks);
                        _variantMappingService.InitializeFromTrackInfo(tracks);
                        _trackMappingsInitialized = true;
                    }
                    _trackInfoLoaded = true;
                    Log.Information("Loaded and cached track info for {Count} tracks", _trackInfoCache.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load track info from API");
            }
        }

        public async Task EnsureTrackMappingsInitializedAsync()
        {
            await LoadTrackInfoAsync(initializeMappings: true).ConfigureAwait(false);
        }

        public List<ApiTrack> GetFilteredTracks(GhostTrackType trackFilter, GhostLocation locationFilter, string searchText = "")
        {
            var filtered = _allTracks.AsEnumerable();

            filtered = trackFilter switch
            {
                GhostTrackType.Retro => filtered.Where(t => t.TrackType == GhostTrackType.Retro),
                GhostTrackType.Custom => filtered.Where(t => t.TrackType == GhostTrackType.Custom),
                _ => filtered
            };

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(t => 
                    t.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    t.Console.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            return filtered.ToList();
        }
    }
}