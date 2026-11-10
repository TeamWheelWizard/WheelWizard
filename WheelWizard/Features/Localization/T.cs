using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace WheelWizard.Localization;

public sealed class T : MarkupExtension
{
    public T() { }

    public T(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]") { Source = TranslationSource.Instance, Mode = BindingMode.OneWay };
    }

    private sealed class TranslationSource : INotifyPropertyChanged
    {
        public static TranslationSource Instance { get; } = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        public string this[string key] => TranslationFunctions.t(key);

        private TranslationSource()
        {
            LocalizationProvider.LanguageChanged += (_, _) =>
            {
                if (Dispatcher.UIThread.CheckAccess())
                    Refresh();
                else
                    Dispatcher.UIThread.Post(Refresh);
            };
        }

        private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
    }
}
