using WheelWizard.Recomp;
using WheelWizard.Settings;

namespace WheelWizard.Services.Launcher;

/// <summary>
/// Resolves the launcher the Home page should drive. The recomp is a Windows-only beta that, when
/// opted into, replaces the Dolphin/Retro Rewind frontend entirely; which launcher that decision
/// selects lives here, so no view has to re-derive it.
/// </summary>
public interface ILauncherProvider
{
    ILauncher GetActiveLauncher();
}

public class LauncherProvider(ISettingsManager settings, IServiceProvider serviceProvider) : ILauncherProvider
{
    // Resolved per call instead of once: the active frontend can change while the app runs, and the
    // recomp launcher is only registered on Windows — which IsRecompModeActive() already guards.
    public ILauncher GetActiveLauncher() =>
        settings.IsRecompModeActive()
            ? serviceProvider.GetRequiredService<RecompLauncher>()
            : serviceProvider.GetRequiredService<RrLauncher>();
}
