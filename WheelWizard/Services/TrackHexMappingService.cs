using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WheelWizard.Helpers;
using WheelWizard.Models;
using Serilog;

namespace WheelWizard.Services;

/// <summary>
/// Resolves ghost folder hashes by grouping tracks via courseId and hashing main SZS bytes.
/// </summary>
public class TrackHexMappingService
{
    private const int CourseIdBase = 256;
    private static readonly Regex ColorCodeRegex = new("\\\\c\\{[^}]+\\}", RegexOptions.Compiled);
    private static readonly Regex MappingLineRegex = new("^(?<name>.+?)\\s*=\\s*(?<hex>[0-9A-Fa-f]{8})$", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumRegex = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, string> _trackNameToHexMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _courseIdToHexMap = new();
    private readonly Dictionary<string, int> _trackNameToCourseIdMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<string>> _courseIdToTrackNamesMap = new();
    private readonly object _lock = new();

    private Dictionary<string, string>? _szsIndex;
    private Dictionary<string, string>? _folderToTrackNameMap;
    private bool _initialized;

    public void InitializeFromTrackInfo(IEnumerable<TrackInfo> trackInfos)
    {
        var tracks = trackInfos?
            .Where(t => t.CourseId > 0)
            .ToList() ?? new List<TrackInfo>();

        if (tracks.Count == 0)
        {
            Log.Warning("Track hash initialization skipped: no valid track info entries were provided");
            return;
        }

        lock (_lock)
        {
            _trackNameToHexMap.Clear();
            _courseIdToHexMap.Clear();
            _trackNameToCourseIdMap.Clear();
            _courseIdToTrackNamesMap.Clear();
            _szsIndex = null;
            _folderToTrackNameMap = null;

            foreach (var group in tracks.GroupBy(t => t.CourseId))
            {
                var courseId = group.Key;
                _courseIdToTrackNamesMap[courseId] = group
                    .OrderBy(t => t.Id)
                    .Select(t => t.Name)
                    .ToList();

                foreach (var track in group)
                    _trackNameToCourseIdMap[track.Name] = courseId;
            }

            _initialized = true;

            Log.Information(
                "Initialized lazy track hash metadata for {TrackCount} tracks across {CourseCount} course groups",
                _trackNameToCourseIdMap.Count,
                _courseIdToTrackNamesMap.Count);
        }
    }

    public string? GetHexValueForTrack(string trackName)
    {
        int courseId;

        lock (_lock)
        {
            _trackNameToHexMap.TryGetValue(trackName, out var hexValue);
            if (hexValue != null)
                return hexValue;

            if (!_trackNameToCourseIdMap.TryGetValue(trackName, out courseId))
                return null;
        }

        return ResolveHexValueForCourse(courseId);
    }

    public string? GetHexValueForTrack(string console, string trackName)
    {
        var fullTrackName = $"{console} {trackName}";
        return GetHexValueForTrack(fullTrackName);
    }

    public Dictionary<string, string> GetAllMappings()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_trackNameToHexMap, StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool IsInitialized()
    {
        lock (_lock)
        {
            return _initialized;
        }
    }

    private static string ComputeGhostFolderHash(string szsPath)
    {
        var bytes = File.ReadAllBytes(szsPath);
        var crc = CrcHelper.ComputeCrc32(bytes, 0, bytes.Length);
        return crc.ToString("X8");
    }

    private string? ResolveHexValueForCourse(int courseId)
    {
        List<string>? trackNames;

        lock (_lock)
        {
            if (_courseIdToHexMap.TryGetValue(courseId, out var cachedHash))
                return cachedHash;

            if (!_courseIdToTrackNamesMap.TryGetValue(courseId, out trackNames) || trackNames.Count == 0)
                return null;
        }

        EnsureLookupSourcesLoaded();

        string? hash = null;
        var slotIndex = courseId - CourseIdBase;

        if (slotIndex >= 0)
        {
            var mainFileName = $"{slotIndex}.szs";
            var szsIndex = GetSzsIndexSnapshot();

            if (szsIndex.TryGetValue(mainFileName, out var mainSzsPath))
            {
                try
                {
                    hash = ComputeGhostFolderHash(mainSzsPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex,
                        "Failed to compute ghost folder hash from SZS '{SzsPath}' for courseId {CourseId}",
                        mainSzsPath,
                        courseId);
                }
            }
        }

        if (hash == null)
        {
            var folderMap = GetFolderToTrackNameMapSnapshot();
            foreach (var trackName in trackNames)
            {
                if (TryGetMappedHash(folderMap, trackName, out var mappedHash))
                {
                    hash = mappedHash;
                    break;
                }
            }
        }

        if (hash == null)
        {
            Log.Warning(
                "Could not resolve ghost hash for courseId {CourseId}; no slot file and no FolderToTrackName match",
                courseId);
            return null;
        }

        lock (_lock)
        {
            _courseIdToHexMap[courseId] = hash;
            foreach (var trackName in trackNames)
                _trackNameToHexMap[trackName] = hash;
        }

        Log.Debug("Resolved ghost hash {Hash} for courseId {CourseId} on demand", hash, courseId);
        return hash;
    }

    private void EnsureLookupSourcesLoaded()
    {
        lock (_lock)
        {
            if (_szsIndex != null && _folderToTrackNameMap != null)
                return;
        }

        var szsIndex = BuildSzsFileIndex();
        var folderToTrackNameMap = BuildFolderToTrackNameMap();

        lock (_lock)
        {
            _szsIndex ??= szsIndex;
            _folderToTrackNameMap ??= folderToTrackNameMap;
        }
    }

    private Dictionary<string, string> GetSzsIndexSnapshot()
    {
        lock (_lock)
        {
            return _szsIndex ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private Dictionary<string, string> GetFolderToTrackNameMapSnapshot()
    {
        lock (_lock)
        {
            return _folderToTrackNameMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> BuildSzsFileIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSzsSearchRoots())
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.szs", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);
                    if (!index.ContainsKey(name))
                        index[name] = file;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed while indexing SZS files under {Root}", root);
            }
        }

        return index;
    }

    private static Dictionary<string, string> BuildFolderToTrackNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ghostsRoot = Path.Combine(PathManager.RiivolutionWhWzFolderPath, "RetroRewind6", "Ghosts");
        var mappingFiles = new[]
        {
            Path.Combine(ghostsRoot, "FolderToTrackNameRT.txt"),
            Path.Combine(ghostsRoot, "FolderToTrackNameCT.txt"),
            Path.Combine(ghostsRoot, "FolderToTrackName.txt")
        };

        foreach (var file in mappingFiles)
        {
            if (!File.Exists(file))
                continue;

            try
            {
                foreach (var rawLine in File.ReadLines(file))
                {
                    var line = ColorCodeRegex.Replace(rawLine, string.Empty).Trim();
                    if (line.Length == 0)
                        continue;

                    var match = MappingLineRegex.Match(line);
                    if (!match.Success)
                        continue;

                    var trackName = match.Groups["name"].Value.Trim();
                    var hash = match.Groups["hex"].Value.ToUpperInvariant();

                    AddMappingIfMissing(map, trackName, hash);
                    AddMappingIfMissing(map, NormalizeTrackName(trackName), hash);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed while parsing {MappingFile}", file);
            }
        }

        return map;
    }

    private static bool TryGetMappedHash(Dictionary<string, string> map, string trackName, out string? hash)
    {
        if (map.TryGetValue(trackName, out var directHash))
        {
            hash = directHash;
            return true;
        }

        var normalized = NormalizeTrackName(trackName);
        if (map.TryGetValue(normalized, out var normalizedHash))
        {
            hash = normalizedHash;
            return true;
        }

        hash = null;
        return false;
    }

    private static string NormalizeTrackName(string value)
    {
        var lowered = value.ToLowerInvariant();
        var cleaned = NonAlphaNumRegex.Replace(lowered, " ");
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AddMappingIfMissing(Dictionary<string, string> map, string key, string hash)
    {
        if (key.Length == 0)
            return;

        if (!map.ContainsKey(key))
            map[key] = hash;
    }

    private static IEnumerable<string> GetSzsSearchRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(PathManager.RiivolutionWhWzFolderPath, "RetroRewind6"),
            PathManager.MyStuffFolderPath,
            PathManager.RrBetaFolderPath
        };

        return roots;
    }
}
