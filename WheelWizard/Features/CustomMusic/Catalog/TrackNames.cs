namespace WheelWizard.CustomMusic.Catalog;

/// <summary>Recognises the same course across consoles: "SNES Rainbow Road", "N64 Rainbow Road" and "Rainbow Road" are one family.</summary>
public static class TrackNames
{
    // Console tags as Retro Rewind writes them in front of retro track names, longest first.
    private static readonly string[] ConsolePrefixes = ["wii u ", "switch ", "tour ", "snes ", "n64 ", "gba ", "gcn ", "3ds ", "ds ", "wii ", "gc ", "gb "];

    /// <summary>
    /// The course name without console tag, version suffix or bracketed note, in lower case
    /// ("SNES Rainbow Road v2 (rSRR)" -> "rainbow road"), or null when nothing is left.
    /// </summary>
    public static string? FamilyKey(string name) => DisplayFamily(name)?.ToLowerInvariant();

    /// <summary>Like <see cref="FamilyKey"/> but keeping the original capitalisation ("Rainbow Road").</summary>
    public static string? DisplayFamily(string name)
    {
        var text = name.Trim();
        // "Luigi Circuit / Mario Circuit" shares music between two courses; the first names the family.
        var slash = text.IndexOf(" / ", StringComparison.Ordinal);
        if (slash > 0)
            text = text[..slash];

        // Bracketed notes: "(Night)", "(all versions)", or the file name added to tell duplicates apart.
        var bracket = text.IndexOf(" (", StringComparison.Ordinal);
        if (bracket > 0)
            text = text[..bracket];

        // Version suffix: "Track v2", "Track v1.1".
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[^1].Length > 1 && words[^1][0] is 'v' or 'V' && words[^1][1..].All(c => char.IsDigit(c) || c == '.'))
            words.RemoveAt(words.Count - 1);
        text = string.Join(' ', words);

        foreach (var prefix in ConsolePrefixes)
        {
            if (text.Length > prefix.Length && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..];
                break;
            }
        }

        return text.Length == 0 ? null : text;
    }
}
