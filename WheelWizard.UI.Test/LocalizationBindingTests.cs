using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WheelWizard.Localization;

namespace WheelWizard.UI.Test;

public class LocalizationBindingTests
{
    [AvaloniaFact]
    public async Task ExistingBindingUpdatesOnUiThreadAndStopsAfterDisposal()
    {
        var previous = LocalizationProvider.Current;
        var service = new EmbeddedYamlLocalizationService();
        LocalizationProvider.Use(service);
        try
        {
            var text = new TextBlock();
            var binding = (BindingBase)new T("action.cancel").ProvideValue(null!);
            var subscription = text.Bind(TextBlock.TextProperty, binding);
            Assert.Equal(service.Translate("action.cancel"), text.Text);
            await Task.Run(() => service.SetLanguage("nl"));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(service.Translate("action.cancel"), text.Text);
            Assert.NotEqual(service.TranslateForLanguage("action.cancel", "en"), text.Text);
            subscription.Dispose();
            var afterDisposal = text.Text;
            service.SetLanguage("fr");
            Assert.Equal(afterDisposal, text.Text);
        }
        finally
        {
            LocalizationProvider.Use(previous);
        }
    }
}
