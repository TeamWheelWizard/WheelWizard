using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace WheelWizard.Localization;

public sealed class T : MarkupExtension
{
    public T() { }

    public T(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new TranslationObservable(Key).ToBinding();

    private sealed class TranslationObservable(string key) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer) => new Subscription(key, observer);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string _key;
        private IObserver<string>? _observer;

        public Subscription(string key, IObserver<string> observer)
        {
            _key = key;
            _observer = observer;
            LocalizationProvider.LanguageChanged += OnLanguageChanged;
            Publish();
        }

        private void OnLanguageChanged(object? sender, EventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess())
                Publish();
            else
                Dispatcher.UIThread.Post(Publish);
        }

        private void Publish() => _observer?.OnNext(TranslationFunctions.t(_key));

        public void Dispose()
        {
            LocalizationProvider.LanguageChanged -= OnLanguageChanged;
            _observer = null;
        }
    }
}
