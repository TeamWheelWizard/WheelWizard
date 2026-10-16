namespace WheelWizard.GameFiles.Domain;

/// <summary>
/// Describes a finished extraction. Only <c>main.dol</c> and <c>StaticR.rel</c> are pinned by hash: every other file is
/// the user's to edit, and Wheel Wizard never checks it.
/// </summary>
public sealed record GameFilesManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string GameId { get; init; }
    public byte Revision { get; init; }
    public string InternalName { get; init; } = "";

    /// <summary>The image this was extracted from. Informational: the extracted game never needs it again.</summary>
    public string SourcePath { get; init; } = "";

    public long SourceLength { get; init; }
    public DateTime SourceLastWriteUtc { get; init; }
    public required string MainDolSha256 { get; init; }
    public required string StaticRelSha256 { get; init; }
    public int ExtractorVersion { get; init; }
    public DateTime ExtractedUtc { get; init; }

    public bool IsSameGame(string gameId, byte revision) =>
        string.Equals(GameId, gameId, StringComparison.OrdinalIgnoreCase) && Revision == revision;
}
