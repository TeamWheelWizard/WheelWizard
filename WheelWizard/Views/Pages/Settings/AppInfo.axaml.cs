using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using WheelWizard.Branding;
using WheelWizard.CustomDistributions;
using WheelWizard.Shared.DependencyInjection;

namespace WheelWizard.Views.Pages.Settings;

public partial class AppInfo : UserControlBase
{
    [Inject]
    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; set; } = null!;

    public AppInfo()
    {
        InitializeComponent();

        RrVersionText.Text = "RR: " + CustomDistributionSingletonService.RetroRewind.GetCurrentVersion();

        var part1 = "Release";
        var part2 = "Unknown OS";
#if DEBUG
        part1 = "Dev";
#endif
        // We intentionally use preprocessor directives (#if, #elif, #endif) instead of Environment.OSVersion
        // because 'part2' represents the OS this code was built for, not the OS it is currently running on.
#if WINDOWS
        part2 = "Windows";
#elif LINUX
        part2 = "Linux";
#elif MACOS
        part2 = "Macos";
#endif
        ReleaseText.Text = $"{part1} - {part2}";
    }

    private void OpenLick_OnClick(object? sender, EventArgs e)
    {
        if (sender is not TemplatedControl control)
            return;
        if (control.Tag == null)
            return;

        ViewUtils.OpenLink(control.Tag.ToString()!);
    }

    protected override void OnInitialized()
    {
        var branding = App.Services.GetRequiredService<IBrandingSingletonService>().Branding;
        WhWzVersionText.Text = $"WhWz: v{branding.Version}";
        base.OnInitialized();
    }
}
