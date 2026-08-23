using WheelWizard.Models.Enums;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// Maps "what is on disk" plus "what is on GitHub" onto the status the home page understands,
/// exactly the way the Retro Rewind distribution does for its own install.
/// </summary>
public static class RecompStatusResolver
{
    /// <summary>
    /// Resolves the recomp status.
    /// </summary>
    /// <param name="gameFileConfigured">Whether WheelWizard has a usable Mario Kart Wii image configured.</param>
    /// <param name="installedVersion">The <c>setupVersion</c> from <c>install-state.json</c>, or null when not installed.</param>
    /// <param name="latestVersion">The newest release tag on GitHub, or null when GitHub could not be reached.</param>
    /// <param name="products">
    /// The typed product state reported by the installed setup host. A missing answer is deliberately not
    /// treated as fresh: WheelWizard must not launch a product whose freshness it could not verify.
    /// </param>
    /// <param name="installationBusy">
    /// Whether the check was refused because the backend's per-installation lock is held elsewhere,
    /// rather than because it found anything wrong.
    /// </param>
    public static WheelWizardStatus Resolve(
        bool gameFileConfigured,
        string? installedVersion,
        string? latestVersion,
        RecompProductsEvent? products,
        bool installationBusy = false
    )
    {
        if (!gameFileConfigured)
            return WheelWizardStatus.ConfigNotFinished;

        if (!RecompVersion.TryParse(installedVersion, out var installed))
        {
            // Without a release we cannot install anything either, so surface that as "no server".
            return RecompVersion.TryParse(latestVersion, out _) ? WheelWizardStatus.NotInstalled : WheelWizardStatus.NoServer;
        }

        // A busy installation is unverifiable, not stale. The lock is held for the whole lifetime of a
        // running game, so falling through to the fail-closed rule below would put an Update button on
        // screen for an update that does not exist, whose only effect is to fail on that same lock.
        // Nothing stale can be launched by trusting the install that was already verified this way:
        // the pre-launch reconciliation runs the check again, under the lock, before any game starts.
        if (installationBusy && products is null)
            return RecompVersion.TryParse(latestVersion, out _) ? WheelWizardStatus.Ready : WheelWizardStatus.NoServerButInstalled;

        // A failed or incomplete check is fail-closed. The installed host can perform a targeted repair
        // without GitHub, so an update remains actionable even offline.
        // A blocked product always requires action, so ActionRequired already covers it.
        if (products is null || products.ActionRequired || !products.Base.IsCurrent || !products.RetroRewind.IsCurrent)
            return WheelWizardStatus.OutOfDate;

        // Installed but GitHub is unreachable: the checked products are still playable offline.
        if (!RecompVersion.TryParse(latestVersion, out var latest))
            return WheelWizardStatus.NoServerButInstalled;

        if (latest.ComparePrecedenceTo(installed) > 0)
            return WheelWizardStatus.OutOfDate;

        return WheelWizardStatus.Ready;
    }
}
