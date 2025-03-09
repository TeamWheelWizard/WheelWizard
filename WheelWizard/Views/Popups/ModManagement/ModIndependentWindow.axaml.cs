using Avalonia;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views.Popups.ModManagement;

public partial class ModIndependentWindow : PopupContent
{
    public ModIndependentWindow(string windowTitle = "Mod Details") : 
        base(true, false, true, windowTitle, new Vector(500, 700))
    {
        InitializeComponent();
    }
    
    public async Task LoadModAsync(int modId, string newDownloadUrl = null)
    {
        await ModDetailViewer.LoadModDetailsAsync(modId, newDownloadUrl);
    }
        
    protected override void BeforeClose()
    {
        ViewUtils.NavigateToPage(new ModsPage());
        base.BeforeClose();
    }
}
