using System;

namespace WheelWizard.WiiManagement.GameExtraction;

internal sealed class GameExtractionMetadata
{
    public string SourcePath { get; set; } = string.Empty;
    public long SourceFileSize { get; set; }
    public long SourceLastWriteTimeUtcTicks { get; set; }
    public DateTime ExtractedAtUtc { get; set; }
    public string MainDolRelativePath { get; set; } = string.Empty;
}
