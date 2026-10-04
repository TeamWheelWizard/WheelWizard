using WheelWizard.Views.Navigation;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups.ModManagement;

public partial class ModIndependentWindow : PopupContent
{
    private ModContent ModDetailViewer { get; }

    private INavigationService Navigation { get; }

    public ModIndependentWindow(ModContent modDetailViewer, INavigationService navigation, string windowTitle = "Mod Details")
        : base(true, false, true, windowTitle)
    {
        ModDetailViewer = modDetailViewer;
        Navigation = navigation;
        InitializeComponent();
        ModDetailHost.Content = ModDetailViewer;
        if (Window.WindowTitle == "Mod Details")
            Window.WindowTitle = t("popup_title.mod_details");
    }

    public async Task LoadModAsync(int modId, string? newDownloadUrl = null)
    {
        await ModDetailViewer.LoadModDetailsAsync(modId, newDownloadUrl);
    }

    protected override void BeforeClose()
    {
        Navigation.NavigateTo<ModsPage>();
        base.BeforeClose();
    }
}
