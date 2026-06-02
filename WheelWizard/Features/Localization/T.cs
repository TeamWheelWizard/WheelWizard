using Avalonia.Markup.Xaml;

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
        return TranslationFunctions.t(Key);
    }
}
