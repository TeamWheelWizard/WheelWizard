using Avalonia.Controls;

namespace WheelWizard.Views.Navigation;

public interface INavigationService
{
    UserControl? CurrentPage { get; }
    event EventHandler<UserControl>? PageChanged;
    void NavigateTo(Type pageType, params object[] arguments);
    void NavigateTo<T>(params object[] arguments)
        where T : UserControl;
}

public sealed class NavigationService(IPageFactory pages) : INavigationService
{
    private int _generation;
    public UserControl? CurrentPage { get; private set; }
    public event EventHandler<UserControl>? PageChanged;

    public void NavigateTo(Type pageType, params object[] arguments)
    {
        var generation = ++_generation;
        var page = pages.Create(pageType, arguments);
        if (generation != _generation)
            return;
        CurrentPage = page;
        PageChanged?.Invoke(this, page);
    }

    public void NavigateTo<T>(params object[] arguments)
        where T : UserControl => NavigateTo(typeof(T), arguments);
}
