using WheelWizard.Models;
using WheelWizard.Services;
using Serilog;

namespace WheelWizard.Services;

/// <summary>
/// Helper service demonstrating usage of the track variant system
/// </summary>
public class GhostSaveHelper
{
    private readonly LocalGhostService _localGhostService;
    private readonly TrackVariantMappingService _variantMappingService;
    private readonly TrackHexMappingService _trackHexMappingService;

    public GhostSaveHelper(
        LocalGhostService localGhostService, 
        TrackVariantMappingService variantMappingService,
        TrackHexMappingService trackHexMappingService)
    {
        _localGhostService = localGhostService;
        _variantMappingService = variantMappingService;
        _trackHexMappingService = trackHexMappingService;
    }

    /// <summary>
    /// Gets the correct ghost folder path for saving/loading ghosts for a specific track
    /// </summary>
    /// <param name="trackName">Full track name (e.g., "GP Bowser's Castle" or "GP Castle Wall")</param>
    /// <param name="cc">CC value (150 or 200)</param>
    /// <returns>Full path to the ghost folder, or null if track not found</returns>
    public string? GetGhostFolderPath(string trackName, int cc)
    {
        return _variantMappingService.GetGhostFolderPath(trackName, cc, _trackHexMappingService, PathManager.GhostsFolderPath);
    }

    /// <summary>
    /// Opens the correct ghost folder in file explorer for the specified track
    /// </summary>
    /// <param name="trackName">Full track name</param>
    /// <param name="cc">Optional CC to open specific CC folder</param>
    public void OpenGhostFolder(string trackName, int? cc = null)
    {
        _localGhostService.OpenTrackFolderInExplorer(trackName, cc);
    }
}