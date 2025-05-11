using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Branding;
using WheelWizard.CustomDistributions;
using WheelWizard.Services.Installation;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups;

namespace WheelWizard.Views.Pages.Settings;

public partial class SettingsPage : UserControlBase
{
    public SettingsPage()
        : this(new WhWzSettings()) { }

    [Inject]
    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; set; } = null!;

    public SettingsPage(UserControl initialSettingsPage)
    {
        InitializeComponent();

        RrVersionText.Text = "RR: " + CustomDistributionSingletonService.RetroRewind.GetCurrentVersion();

        var part1 = "Release";
        var part2 = "Unknown OS";
#if DEBUG
        part1 = "Dev";
        DevButton.IsVisible = true;
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
        SettingsContent.Content = initialSettingsPage;
    }

    protected override void OnInitialized()
    {
        var branding = App.Services.GetRequiredService<IBrandingSingletonService>().Branding;
        WhWzVersionText.Text = $"WhWz: v{branding.Version}";

        base.OnInitialized();
    }

    private void TopBarRadio_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
            return;

        // As long as the Ks... files are next to this file, it works.
        var namespaceName = GetType().Namespace;
        var typeName = $"{namespaceName}.{radioButton.Tag}";
        var type = Type.GetType(typeName);
        if (type == null || !typeof(UserControl).IsAssignableFrom(type))
            return;

        if (Activator.CreateInstance(type) is not UserControl instance)
            return;

        SettingsContent.Content = instance;
    }

    private void DevButton_OnClick(object? sender, RoutedEventArgs e) => new DevToolWindow().Show();
}
