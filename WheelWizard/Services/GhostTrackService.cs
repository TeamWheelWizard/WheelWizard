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
        private List<ApiTrack> _allTracks = new();
        private Dictionary<int, TrackInfo> _trackInfoCache = new();
        private bool _tracksLoaded = false;
        private bool _trackInfoLoaded = false;

        public GhostTrackService(HttpClient httpClient, TrackHexMappingService hexMappingService)
        {
            _httpClient = httpClient;
            _hexMappingService = hexMappingService;
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
                
                await LoadTrackInfoAsync();
                
                const string worldRecordsUrl = "https://rwfc.net/api/timetrial/worldrecords/all?glitchAllowed=true&cc=150";
                var response = await _httpClient.GetStringAsync(worldRecordsUrl);
                
                var worldRecords = JsonSerializer.Deserialize<WorldRecordsResponse[]>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (worldRecords != null)
                {
                    _allTracks = worldRecords
                        .Where(wr => wr.TrackId > 0)
                        .Select(wr => 
                        {
                            _trackInfoCache.TryGetValue(wr.TrackId, out var trackInfo);
                            var track = ApiTrack.FromWorldRecordAndTrackInfo(wr, trackInfo);
                            
                            track.HexValue = _hexMappingService.GetHexValueForTrack(wr.TrackName) ?? 
                                           _hexMappingService.GetHexValueForTrack(track.Console, wr.TrackName) ?? 
                                           wr.TrackId.ToString("X8");
                            
                            return track;
                        })
                        .OrderBy(t => t.TrackType)
                        .ThenBy(t => t.Name)
                        .ToList();
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

        private async Task LoadTrackInfoAsync()
        {
            if (_trackInfoLoaded && _trackInfoCache.Count > 0)
            {
                Log.Debug("Using cached track info ({Count} tracks)", _trackInfoCache.Count);
                return;
            }

            try
            {
                Log.Information("Loading track info from tracks API (first time)");
                const string tracksUrl = "https://rwfc.net/api/timetrial/tracks";
                var response = await _httpClient.GetStringAsync(tracksUrl);
                
                var tracks = JsonSerializer.Deserialize<TrackInfo[]>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tracks != null)
                {
                    _trackInfoCache = tracks.ToDictionary(t => t.Id);
                    _trackInfoLoaded = true;
                    Log.Information("Loaded and cached track info for {Count} tracks", _trackInfoCache.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load track info from API");
            }
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