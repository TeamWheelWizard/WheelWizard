using System.Diagnostics.CodeAnalysis;
using Semver;

namespace WheelWizard.Recomp;

/// <summary>
/// Version parsing shared by the release resolver and the status resolver.
/// Recomp releases are tagged <c>v&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;</c>, while
/// <c>install-state.json</c> stores the bare semantic version.
/// </summary>
public static class RecompVersion
{
    /// <summary>
    /// Parses a release tag or an <c>install-state.json</c> version, tolerating the leading <c>v</c>.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        return SemVersion.TryParse(trimmed, SemVersionStyles.Any, out version);
    }
}
