using WheelWizard.Models;
using WheelWizard.Services;
using Serilog;

namespace WheelWizard.Services;

/// <summary>
/// Service for discovering and parsing local ghost files from the Dolphin/Wii file system
/// </summary>
public class LocalGhostService
{
    private readonly Dictionary<uint, LocalTrackGhosts> _cachedTrackGhosts = new();
    private bool _hasScannedFolder = false;

    /// <summary>
    /// Scans the Dolphin ghosts folder and returns local ghost data for all tracks
    /// </summary>
    public async Task<Dictionary<uint, LocalTrackGhosts>> GetAllLocalGhostsAsync()
    {
        if (_hasScannedFolder && _cachedTrackGhosts.Count > 0)
        {
            Log.Information("Using cached local ghost data for {TrackCount} tracks", _cachedTrackGhosts.Count);
            return _cachedTrackGhosts;
        }

        await Task.Run(() => ScanGhostsFolder());
        _hasScannedFolder = true;
        
        Log.Information("Loaded local ghost data for {TrackCount} tracks", _cachedTrackGhosts.Count);
        return _cachedTrackGhosts;
    }

    /// <summary>
    /// Gets local ghost data for a specific track by its ID
    /// </summary>
    public async Task<LocalTrackGhosts?> GetTrackGhostsAsync(uint trackId, string hexValue)
    {
        var allGhosts = await GetAllLocalGhostsAsync();
        
        if (allGhosts.TryGetValue(trackId, out var trackGhosts))
        {
            return trackGhosts;
        }

        return ScanTrackFolder(trackId, hexValue);
    }

    /// <summary>
    /// Opens the ghost folder for a specific track in file explorer
    /// </summary>
    public void OpenTrackFolderInExplorer(string hexValue)
    {
        try
        {
            var trackFolderPath = Path.Combine(PathManager.GhostsFolderPath, hexValue.ToLowerInvariant());
            
            if (!Directory.Exists(trackFolderPath))
            {
                Log.Warning("Track ghost folder does not exist: {FolderPath}", trackFolderPath);
                Directory.CreateDirectory(trackFolderPath);
            }

            FilePickerHelper.OpenFolderInFileManager(trackFolderPath);
            Log.Information("Opened track ghost folder: {FolderPath}", trackFolderPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open track ghost folder for hex: {HexValue}", hexValue);
        }
    }

    private void ScanGhostsFolder()
    {
        _cachedTrackGhosts.Clear();
        
        try
        {
            var ghostsPath = PathManager.GhostsFolderPath;
            
            if (!Directory.Exists(ghostsPath))
            {
                Log.Information("Ghosts folder does not exist: {Path}", ghostsPath);
                return;
            }

            var hexFolders = Directory.GetDirectories(ghostsPath)
                .Where(dir => IsValidHexFolder(Path.GetFileName(dir)))
                .ToList();

            Log.Information("Scanning {FolderCount} hex folders in ghosts directory", hexFolders.Count);

            foreach (var hexFolder in hexFolders)
            {
                var hexName = Path.GetFileName(hexFolder).ToLowerInvariant();
                var trackGhosts = ScanHexFolder(hexFolder, hexName);
                
                if (trackGhosts?.HasGhosts == true)
                {
                    _cachedTrackGhosts[trackGhosts.TrackId] = trackGhosts;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error scanning ghosts folder: {Path}", PathManager.GhostsFolderPath);
        }
    }

    private LocalTrackGhosts? ScanTrackFolder(uint trackId, string hexValue)
    {
        try
        {
            var hexFolder = Path.Combine(PathManager.GhostsFolderPath, hexValue.ToLowerInvariant());
            
            if (!Directory.Exists(hexFolder))
                return null;

            return ScanHexFolder(hexFolder, hexValue.ToLowerInvariant());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error scanning track folder for track {TrackId}, hex {HexValue}", trackId, hexValue);
            return null;
        }
    }

    private LocalTrackGhosts? ScanHexFolder(string hexFolderPath, string hexName)
    {
        var trackGhosts = new LocalTrackGhosts
        {
            HexFolderName = hexName
        };

        try
        {
            ScanCcFolder(Path.Combine(hexFolderPath, "150"), trackGhosts.Ghosts150, 150, false);
            ScanCcFolder(Path.Combine(hexFolderPath, "200"), trackGhosts.Ghosts200, 200, false);
            
            var variantPath = Path.Combine(hexFolderPath, "1");
            if (Directory.Exists(variantPath))
            {
                ScanCcFolder(Path.Combine(variantPath, "150"), trackGhosts.VariantGhosts150, 150, true);
                ScanCcFolder(Path.Combine(variantPath, "200"), trackGhosts.VariantGhosts200, 200, true);
            }

            var firstGhost = trackGhosts.Ghosts150.FirstOrDefault() ?? 
                           trackGhosts.Ghosts200.FirstOrDefault() ?? 
                           trackGhosts.VariantGhosts150.FirstOrDefault() ?? 
                           trackGhosts.VariantGhosts200.FirstOrDefault();
            
            if (firstGhost != null)
            {
                trackGhosts.TrackId = firstGhost.TrackId;
                trackGhosts.TrackName = firstGhost.TrackName;
            }

            return trackGhosts.HasGhosts ? trackGhosts : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error scanning hex folder: {FolderPath}", hexFolderPath);
            return null;
        }
    }

    private void ScanCcFolder(string ccFolderPath, List<LocalGhostData> ghostList, int cc, bool isVariant)
    {
        try
        {
            if (!Directory.Exists(ccFolderPath))
            {
                Log.Debug("CC folder does not exist: {FolderPath}", ccFolderPath);
                return;
            }

            var rkgFiles = Directory.GetFiles(ccFolderPath, "*.rkg", SearchOption.TopDirectoryOnly);
            Log.Debug("Found {FileCount} .rkg files in {FolderPath}", rkgFiles.Length, ccFolderPath);
            
            var successfullyParsed = 0;
            foreach (var rkgFile in rkgFiles)
            {
                Log.Debug("Attempting to parse RKG file: {FilePath}", rkgFile);
                var ghostData = RkgParser.ParseRkgFile(rkgFile);
                if (ghostData != null)
                {
                    ghostData.Cc = cc;
                    ghostData.IsVariant = isVariant;
                    ghostList.Add(ghostData);
                    successfullyParsed++;
                    Log.Debug("Successfully parsed RKG: {FilePath} -> Track: {TrackName}, Time: {Time}", 
                        rkgFile, ghostData.TrackName, ghostData.TotalTimeDisplay);
                }
                else
                {
                    Log.Warning("Failed to parse RKG file: {FilePath}", rkgFile);
                }
            }

            Log.Debug("Parsed {SuccessCount}/{TotalCount} RKG files in {FolderPath}", 
                successfullyParsed, rkgFiles.Length, ccFolderPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error scanning CC folder: {FolderPath}", ccFolderPath);
        }
    }

    private static bool IsValidHexFolder(string folderName)
    {
        return !string.IsNullOrEmpty(folderName) && 
               folderName.Length == 8 && 
               folderName.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    /// <summary>
    /// Force a rescan of the ghosts folder (clears cache)
    /// </summary>
    public void RefreshGhostData()
    {
        _cachedTrackGhosts.Clear();
        _hasScannedFolder = false;
        Log.Information("Cleared local ghost cache, will rescan on next request");
    }
}