using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// What the install service should do about the Retro-WFC payload for the next setup operation.
/// </summary>
public enum RecompRetroWfcPayloadDecision
{
    /// <summary>Pass <c>--download-retro-wfc-payload</c>.</summary>
    Download,

    /// <summary>Pass <c>--skip-retro-wfc-payload</c> without asking: the installation is already offline-only.</summary>
    Skip,

    /// <summary>A fresh Retro Rewind build cannot get its payload; only the user can choose offline-only.</summary>
    AskUser,
}

/// <summary>
/// The pure decision behind the payload option, kept free of I/O so the cases can be tested directly.
/// An installation that already embeds a payload always asks the host to download, because the host
/// falls back to its own verified copy when the service is down. A skipped installation upgrades when
/// the service is back and otherwise stays as it is. Only a Retro Rewind build with nothing to fall
/// back to is the user's call.
/// </summary>
public static class RecompRetroWfcPayloadPolicy
{
    /// <summary>
    /// Whether the decision depends on the payload service at all. When it does not, no probe should run:
    /// a normal installation must never wait on a dead endpoint just to be told what it already knows.
    /// </summary>
    public static bool NeedsServiceProbe(RecompInstallState? state, bool hasRetroRewindSource) =>
        hasRetroRewindSource && !HasVerifiedPayload(state);

    /// <summary>
    /// Decides the payload option. <paramref name="serviceReachable"/> is only consulted when
    /// <see cref="NeedsServiceProbe"/> says so; pass any value otherwise.
    /// </summary>
    public static RecompRetroWfcPayloadDecision Decide(RecompInstallState? state, bool hasRetroRewindSource, bool serviceReachable)
    {
        if (!NeedsServiceProbe(state, hasRetroRewindSource) || serviceReachable)
            return RecompRetroWfcPayloadDecision.Download;

        return state is { IsRetroWfcPayloadSkipped: true } ? RecompRetroWfcPayloadDecision.Skip : RecompRetroWfcPayloadDecision.AskUser;
    }

    /// <summary>
    /// Whether the installed Retro Rewind product carries a payload the host can fall back to. A state
    /// that predates the mode field but reports Retro Rewind installed counts: every host so far has
    /// only ever built Retro Rewind with a downloaded payload, so absent means downloaded, never skipped.
    /// </summary>
    private static bool HasVerifiedPayload(RecompInstallState? state)
    {
        if (state is null)
            return false;
        if (string.Equals(state.RetroWfcPayloadMode, "downloaded", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.IsNullOrWhiteSpace(state.RetroWfcPayloadMode) && state.RetroRewindInstalled;
    }
}
