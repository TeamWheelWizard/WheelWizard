using WheelWizard.Recomp;
using WheelWizard.Settings;

namespace WheelWizard.Launching;

/// <summary>
/// Resolves the launcher the Home page should drive. The recomp is a beta (Windows and Linux) that, when
/// opted into, replaces the Dolphin/Retro Rewind frontend entirely; which launcher that decision
/// selects lives here, so no view has to re-derive it.
/// </summary>
public interface ILauncherProvider
{
    ILauncher GetActiveLauncher();
}

public class LauncherProvider(ISettingsManager settings, Func<RrLauncher> createRetroRewind, Func<RecompLauncher?> createRecomp)
    : ILauncherProvider
{
    public ILauncher GetActiveLauncher() =>
        settings.IsRecompModeActive()
            ? createRecomp() ?? throw new InvalidOperationException("The recomp launcher is unavailable on this platform.")
            : createRetroRewind();
}
