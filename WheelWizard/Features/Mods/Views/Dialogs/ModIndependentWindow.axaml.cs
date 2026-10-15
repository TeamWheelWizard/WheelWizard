using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Mods.Views.Dialogs;

public partial class ModIndependentWindow : ModalContent
{
    private ModContent ModDetailViewer { get; }

    public ModIndependentWindow(ModContent modDetailViewer, string windowTitle = "Mod Details")
        : base(windowTitle)
    {
        ModDetailViewer = modDetailViewer;
        InitializeComponent();
        IsDismissible = true;
        ModDetailHost.Content = ModDetailViewer;
        if (ModalTitle == "Mod Details")
            ModalTitle = t("popup_title.mod_details");
    }

    public async Task LoadModAsync(int modId, string? newDownloadUrl = null)
    {
        await ModDetailViewer.LoadModDetailsAsync(modId, newDownloadUrl);
    }
}
