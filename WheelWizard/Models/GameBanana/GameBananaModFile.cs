// ReSharper disable InconsistentNaming

namespace WheelWizard.Models.GameBanana;

public class GameBananaModFile
{
    public int _idRow { get; set; }
    public string _sFile { get; set; }
    public int _nFilesize { get; set; }
    public string _sDownloadUrl { get; set; }
    public string _sAnalysisResult { get; set; } // Analysis result of the file (e.g., "clean", "contains_exe")
    public bool _bContainsExe { get; set; }
    public string _sMd5Checksum { get; set; }
}
