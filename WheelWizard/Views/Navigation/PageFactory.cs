using Avalonia.Controls;

namespace WheelWizard.Views.Navigation;

public interface IPageFactory
{
    UserControl Create(Type pageType, params object[] arguments);
    T Create<T>(params object[] arguments)
        where T : UserControl;
}

/// <summary>Composition boundary for pages whose constructors combine services with navigation arguments.</summary>
public sealed class PageFactory(IServiceProvider services) : IPageFactory
{
    public UserControl Create(Type pageType, params object[] arguments)
    {
        if (!typeof(UserControl).IsAssignableFrom(pageType))
            throw new ArgumentException("Navigation requires a page control.", nameof(pageType));
        return (UserControl)ActivatorUtilities.CreateInstance(services, pageType, arguments);
    }

    public T Create<T>(params object[] arguments)
        where T : UserControl => (T)Create(typeof(T), arguments);
}
