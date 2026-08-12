using System.Text.RegularExpressions;

namespace WheelWizard.DolphinInstaller;

public enum DolphinVersionStatus
{
    /// <summary>The installed version carries the disc-image security fixes.</summary>
    Supported,

    /// <summary>The installed version is known, and known to predate the fixes.</summary>
    Outdated,

    /// <summary>We could not read or recognize the version, so we have to assume the best.</summary>
    Unknown,
}

/// <summary>
/// Understands Dolphin's YYMM version scheme, e.g. "2606" (release), "2606a" (point release) or
/// "2606-235" (dev build, 235 commits after the 2606 release).
/// </summary>
public static class DolphinVersion
{
    /// <summary>Dolphin moved from the old "5.0" style to YYMM releases with 2412.</summary>
    private const int FirstYearMonthRelease = 2412;

    /// <summary>The oldest builds that carry the fix: release 2606a, or dev build 2606-235.</summary>
    private const int MinimumYearMonth = 2606;
    private const char MinimumRevision = 'a';
    private const int MinimumDevBuild = 235;

    /// <summary>The minimum, formatted the way the release notes write it, for use in user-facing text.</summary>
    public const string MinimumDisplayText = "2606a";

    /// <summary>Matches a YYMM version, optionally with a point-release letter or a "-N" dev build.</summary>
    /// <remarks>
    /// The lookarounds keep this from matching digits inside an old-style version: in "5.0-21460" the
    /// engine would otherwise happily take "2146" out of the middle of the build number.
    /// </remarks>
    private static readonly Regex VersionPattern = new(
        @"(?<![\w.])(?<yearMonth>\d{4})(?<revision>[a-z]?)(?:-(?<devBuild>\d+))?(?![\w.])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>Matches the old "5.0" / "5.0-21460" / "4.0.2" scheme, which always predates the minimum.</summary>
    private static readonly Regex LegacyPattern = new(@"(?<![\w.])[0-5]\.\d+(?:\.\d+)*(?:-\d+)?(?![\w])", RegexOptions.Compiled);

    /// <summary>
    /// Judges any version text Dolphin hands us, be it a bare "2606a" or the "Dolphin 2606-235" that
    /// <c>--version</c> prints. Also returns the version as found in the text, for showing to the user.
    /// </summary>
    public static DolphinVersionStatus GetStatus(string? text, out string? foundVersion)
    {
        foundVersion = null;
        if (string.IsNullOrWhiteSpace(text))
            return DolphinVersionStatus.Unknown;

        var match = VersionPattern.Match(text);
        var yearMonth = match.Success ? int.Parse(match.Groups["yearMonth"].Value) : 0;

        // A four-digit number only counts as a version when it is a plausible year-month
        // from the era where this scheme actually existed.
        if (yearMonth >= FirstYearMonthRelease && yearMonth % 100 is >= 1 and <= 12)
        {
            foundVersion = match.Value;
            return IsAtLeastMinimum(yearMonth, match) ? DolphinVersionStatus.Supported : DolphinVersionStatus.Outdated;
        }

        var legacyMatch = LegacyPattern.Match(text);
        if (legacyMatch.Success)
        {
            foundVersion = legacyMatch.Value;
            return DolphinVersionStatus.Outdated;
        }

        return DolphinVersionStatus.Unknown;
    }

    public static DolphinVersionStatus GetStatus(string? text) => GetStatus(text, out _);

    private static bool IsAtLeastMinimum(int yearMonth, Match match)
    {
        if (yearMonth != MinimumYearMonth)
            return yearMonth > MinimumYearMonth;

        // Within 2606 itself the two build families have their own cutoff: dev builds count commits,
        // point releases count letters, and the two are never compared to each other.
        var devBuildGroup = match.Groups["devBuild"];
        if (devBuildGroup.Success)
            return int.Parse(devBuildGroup.Value) >= MinimumDevBuild;

        var revision = match.Groups["revision"].Value;
        return revision.Length > 0 && char.ToLowerInvariant(revision[0]) >= MinimumRevision;
    }
}
