using Avalonia.Controls;
using WheelWizard.Branding;
using WheelWizard.CustomDistributions;
using WheelWizard.Shared;

namespace WheelWizard.Settings.Views;

public partial class SettingsVersionInfo : UserControl
{
    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; }

    private IBrandingSingletonService BrandingSingletonService { get; }

    public SettingsVersionInfo(
        ICustomDistributionSingletonService customDistributionSingletonService,
        IBrandingSingletonService brandingSingletonService
    )
    {
        CustomDistributionSingletonService = customDistributionSingletonService;
        BrandingSingletonService = brandingSingletonService;
        InitializeComponent();

        RrVersionText.Text = "RR: " + CustomDistributionSingletonService.RetroRewind.GetCurrentVersion();

        ReleaseText.Text = DevelopmentMode.IsEnabled ? "Dev" : "Release";
    }

    protected override void OnInitialized()
    {
        var branding = BrandingSingletonService.Branding;
        WhWzVersionText.Text = $"WhWz: v{branding.Version}";
        base.OnInitialized();
    }
}
