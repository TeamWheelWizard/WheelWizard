namespace WheelWizard.DiscImages.Domain;

public enum DiscImageFormat
{
    Iso,
    Gcz,
    Ciso,
    Wbfs,
    Tgc,
    Wia,
    Rvz,
    Nfs,
}

/// <summary>What the header of a game disc image says about the game on it.</summary>
public sealed record DiscImageInfo(string GameId, string InternalName, byte Revision, DiscImageFormat Format);
