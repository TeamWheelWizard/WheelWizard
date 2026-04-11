using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WheelWizard.Models;
using Serilog;

namespace WheelWizard.Services;

/// <summary>
/// Resolves variant/main track relationships by grouping API tracks with shared courseId.
/// </summary>
public class TrackVariantMappingService
{
    private readonly Dictionary<string, string> _variantToMainTrackMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _variantTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _mainTrackToVariantsMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void InitializeFromTrackInfo(IEnumerable<TrackInfo> trackInfos)
    {
        var tracks = trackInfos?
            .Where(t => t.CourseId > 0)
            .ToList() ?? new List<TrackInfo>();

        if (tracks.Count == 0)
        {
            Log.Warning("Variant mapping initialization skipped: no valid track info entries were provided");
            return;
        }

        lock (_lock)
        {
            _variantToMainTrackMap.Clear();
            _variantTracks.Clear();
            _mainTrackToVariantsMap.Clear();

            foreach (var group in tracks.GroupBy(t => t.CourseId))
            {
                var grouped = group.OrderBy(t => t.Id).ToList();
                var mainTrack = grouped.First();

                if (!_mainTrackToVariantsMap.ContainsKey(mainTrack.Name))
                    _mainTrackToVariantsMap[mainTrack.Name] = new List<string>();

                foreach (var track in grouped.Skip(1))
                {
                    _variantToMainTrackMap[track.Name] = mainTrack.Name;
                    _variantTracks.Add(track.Name);
                    _mainTrackToVariantsMap[mainTrack.Name].Add(track.Name);
                }
            }

            Log.Information(
                "Initialized variant mapping with {VariantCount} variants under {MainCount} main tracks",
                _variantToMainTrackMap.Count,
                _mainTrackToVariantsMap.Count(kvp => kvp.Value.Count > 0));
        }
    }

    public bool IsVariantTrack(string trackName)
    {
        lock (_lock)
        {
            return _variantTracks.Contains(trackName);
        }
    }

    public string GetMainTrackName(string variantTrackName)
    {
        lock (_lock)
        {
            if (_variantToMainTrackMap.TryGetValue(variantTrackName, out var mainTrack))
                return mainTrack;

            return variantTrackName;
        }
    }

    public List<string> GetVariantsForMainTrack(string mainTrackName)
    {
        lock (_lock)
        {
            if (_mainTrackToVariantsMap.TryGetValue(mainTrackName, out var variants))
                return new List<string>(variants);

            return new List<string>();
        }
    }

    public string? GetHexValueForTrack(string trackName, TrackHexMappingService trackHexMappingService)
    {
        var mainTrackName = GetMainTrackName(trackName);
        return trackHexMappingService.GetHexValueForTrack(mainTrackName);
    }

    public string? GetGhostFolderPath(string trackName, int cc, TrackHexMappingService trackHexMappingService, string ghostsBasePath)
    {
        var hexValue = GetHexValueForTrack(trackName, trackHexMappingService);
        if (hexValue == null)
            return null;

        var hexFolder = Path.Combine(ghostsBasePath, hexValue.ToLowerInvariant());
        return IsVariantTrack(trackName)
            ? Path.Combine(hexFolder, "1", cc.ToString())
            : Path.Combine(hexFolder, cc.ToString());
    }

    public void AddVariantMapping(string mainTrackName, string variantTrackName)
    {
        lock (_lock)
        {
            _variantToMainTrackMap[variantTrackName] = mainTrackName;
            _variantTracks.Add(variantTrackName);

            if (!_mainTrackToVariantsMap.ContainsKey(mainTrackName))
                _mainTrackToVariantsMap[mainTrackName] = new List<string>();

            if (!_mainTrackToVariantsMap[mainTrackName].Any(v => string.Equals(v, variantTrackName, StringComparison.OrdinalIgnoreCase)))
                _mainTrackToVariantsMap[mainTrackName].Add(variantTrackName);
        }
    }

    public void RemoveVariantMapping(string variantTrackName)
    {
        lock (_lock)
        {
            if (!_variantToMainTrackMap.TryGetValue(variantTrackName, out var mainTrackName))
                return;

            _variantToMainTrackMap.Remove(variantTrackName);
            _variantTracks.Remove(variantTrackName);

            if (_mainTrackToVariantsMap.TryGetValue(mainTrackName, out var variants))
            {
                variants.RemoveAll(v => string.Equals(v, variantTrackName, StringComparison.OrdinalIgnoreCase));
                if (variants.Count == 0)
                    _mainTrackToVariantsMap.Remove(mainTrackName);
            }
        }
    }

    public Dictionary<string, string> GetAllVariantMappings()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_variantToMainTrackMap, StringComparer.OrdinalIgnoreCase);
        }
    }
}
