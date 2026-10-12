using Avalonia.Controls;

namespace WheelWizard.Views.Shell.Navigation;

public interface INavigationService
{
    UserControl? CurrentPage { get; }
    event EventHandler<UserControl>? PageChanged;
    void NavigateTo(Type pageType, params object[] arguments);
    void NavigateTo<T>(params object[] arguments)
        where T : UserControl;

    /// <summary>Navigates without asking the current page (see <see cref="INavigationGuard"/>), e.g. after it saved.</summary>
    void NavigateAway(Type pageType, params object[] arguments);
}

/// <summary>A page that may want to stop you from leaving it, e.g. because you'd lose unsaved work.</summary>
public interface INavigationGuard
{
    /// <summary>Whether leaving now would lose something (also checked before closing the app).</summary>
    bool HasUnsavedWork { get; }

    /// <summary>Asks whether to leave anyway; only called when <see cref="HasUnsavedWork"/>.</summary>
    Task<bool> ConfirmLeaveAsync();
}

/// <summary>Implement on a page to keep the main sidebar collapsed and locked while it is shown. Leaving restores the user's preference.</summary>
public interface ILockedSidebarPage;

public sealed class NavigationService(IPageFactory pages) : INavigationService
{
    private int _generation;
    public UserControl? CurrentPage { get; private set; }
    public event EventHandler<UserControl>? PageChanged;

    public void NavigateTo(Type pageType, params object[] arguments)
    {
        if (CurrentPage is INavigationGuard { HasUnsavedWork: true } guard)
        {
            _ = NavigateAfterConfirmAsync(guard, pageType, arguments);
            return;
        }

        NavigateAway(pageType, arguments);
    }

    public void NavigateTo<T>(params object[] arguments)
        where T : UserControl => NavigateTo(typeof(T), arguments);

    public void NavigateAway(Type pageType, params object[] arguments)
    {
        var generation = ++_generation;
        var page = pages.Create(pageType, arguments);
        if (generation != _generation)
            return;
        CurrentPage = page;
        PageChanged?.Invoke(this, page);
    }

    private async Task NavigateAfterConfirmAsync(INavigationGuard guard, Type pageType, object[] arguments)
    {
        var generation = _generation;
        if (!await guard.ConfirmLeaveAsync())
            return;
        // Something else navigated while the question was open.
        if (generation != _generation || !ReferenceEquals(CurrentPage, guard))
            return;
        NavigateAway(pageType, arguments);
    }
}
