using WheelWizard.Resources.Languages;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups.ModManagement;

public partial class ModIndependentWindow : PopupContent
{
    public ModIndependentWindow(string windowTitle = "Mod Details")
        : base(true, false, true, windowTitle)
    {
        InitializeComponent();
        if (Window.WindowTitle == "Mod Details")
            Window.WindowTitle = Common.PopupTitle_ModDetails;
    }

    public async Task LoadModAsync(int modId, string? newDownloadUrl = null)
    {
        await ModDetailViewer.LoadModDetailsAsync(modId, newDownloadUrl);
    }

    protected override void BeforeClose()
    {
        NavigationManager.NavigateTo<ModsPage>();
        base.BeforeClose();
    }
}
