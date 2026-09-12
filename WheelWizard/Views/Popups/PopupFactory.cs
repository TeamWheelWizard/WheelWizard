using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups;

public interface IPopupFactory
{
    T Create<T>(params object[] arguments)
        where T : PopupContent;
}

/// <summary>Constructs popup content with its services and explicit caller-supplied arguments.</summary>
public sealed class PopupFactory(IServiceProvider services) : IPopupFactory
{
    public T Create<T>(params object[] arguments)
        where T : PopupContent => ActivatorUtilities.CreateInstance<T>(services, arguments);
}
