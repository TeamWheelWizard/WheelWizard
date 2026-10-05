using Avalonia.Controls;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Pages.Settings;

namespace WheelWizard.Views.Navigation;

public interface INavigationService
{
    UserControl? CurrentPage { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    void GoBack();
    void GoForward();
    event EventHandler<UserControl>? PageChanged;
    void NavigateTo(Type pageType, params object[] arguments);
    void NavigateTo<T>(params object[] arguments)
        where T : UserControl;
}

public sealed class NavigationService(IPageFactory pages) : INavigationService
{
    private int _generation;
    private readonly List<(Type PageType, object[] Arguments)> _history = [];
    private int _historyIndex = -1;
    public UserControl? CurrentPage { get; private set; }
    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex < _history.Count - 1;
    public event EventHandler<UserControl>? PageChanged;

    public void NavigateTo(Type pageType, params object[] arguments) =>
        Navigate(pageType, pageType == typeof(SettingsPage) && arguments.Length == 0 ? [typeof(WhWzSettings)] : arguments);

    public void GoBack()
    {
        if (CanGoBack)
            NavigateHistory(_historyIndex - 1);
    }

    public void GoForward()
    {
        if (CanGoForward)
            NavigateHistory(_historyIndex + 1);
    }

    private void NavigateHistory(int index)
    {
        var entry = _history[index];
        Navigate(entry.PageType, entry.Arguments, index);
    }

    private void Navigate(Type pageType, object[] arguments, int? historyIndex = null)
    {
        var generation = ++_generation;
        if (historyIndex is null && _historyIndex >= 0)
        {
            var current = _history[_historyIndex];
            if (current.PageType == pageType && current.Arguments.SequenceEqual(arguments))
                return;
        }
        var page = pages.Create(pageType, arguments);
        if (generation != _generation)
            return;
        if (historyIndex is { } index)
            _historyIndex = index;
        else
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add((pageType, arguments.ToArray()));
            _historyIndex = _history.Count - 1;
        }
        CurrentPage = page;
        PageChanged?.Invoke(this, page);
    }

    public void NavigateTo<T>(params object[] arguments)
        where T : UserControl => NavigateTo(typeof(T), arguments);
}
