using WheelWizard.Models.GameBanana;

public class GameBananaSearchResults
{
    public Metadata _aMetadata { get; set; } // Metadata for the API response (e.g., total records, pagination)

    public List<GameBananaModDetails>
        _aRecords { get; set; } // List of records representing mods or other GameBanana content

    public class Metadata
    {
        public int _nRecordCount { get; set; }
        public int _nPerpage { get; set; } // Number of records per page returned by the API
        public bool _bIsComplete { get; set; }
    }
    
    //public List<ModRecord> _aRecords { get; set; } // List of records representing mods or other GameBanana content
}

/*
public class ModRecord
{
public string? OverrideDownloadUrl { get; set; }
public int _idRow { get; set; }

public string _sModelName { get; set; }
public string _sSingularTitle { get; set; }
public string _sName { get; set; }
public string _sProfileUrl { get; set; }
public long _tsDateAdded { get; set; }
public long _tsDateModified { get; set; }
public bool _bHasFiles { get; set; }
public List<string> _aTags { get; set; } // (e.g., "Stable", "In Development")
public int _nLikeCount { get; set; }
public int _nViewCount { get; set; }

public GameBananaPreviewMedia _aPreviewMedia { get; set; }
public GameBananaSubmitter _aSubmitter { get; set; }
public GameBananaGame _aGame { get; set; }
public GamebananaCatagory _aCategory { get; set; }

public string _sText { get; set; } // Mod description or text (from _sText or _sDescription)
public string _sDescription { get; set; }  // Some mods may also use _sDescription, so you can handle both

public License _aLicense { get; set; }
public TrashInfo _aTrashInfo { get; set; } // only if the mod is trashed
public List<GameBananaModFile> _aFiles { get; set; }
public List<string> _aEmbeddedMedia { get; set; }

public string _sDevelopmentState { get; set; } // (e.g., "In Development", "Final/Stable")
public int _iCompletionPercentage { get; set; } // Percentage of completion if it's still being developed (0–100%)
// URL of the first image
public string FirstImageUrl => _aPreviewMedia?._aImages is { Count: > 0 } ? $"{_aPreviewMedia._aImages[0]._sBaseUrl}/{_aPreviewMedia._aImages[0]._sFile}" : string.Empty;

public class License
{
    public string _sLicenseUrl { get; set; }   // URL to the Creative Commons license details
    public LicenseChecklist _aLicenseChecklist { get; set; }  // Details on what users can or cannot do with the mod

    public class LicenseChecklist
    {
        public List<string> yes { get; set; } // Actions allowed without asking for permission
        public List<string> ask { get; set; } // Actions requiring permission from the mod author
        public List<string> no { get; set; }  // Actions that are not allowed at all
    }
}

public class TrashInfo
{
    public string _sReason { get; set; }
    public long _tsTrashDate { get; set; } // Date when the mod was trashed (timestamp)
}
*/

