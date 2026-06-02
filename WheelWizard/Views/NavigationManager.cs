using Avalonia.Controls;

namespace WheelWizard.Views;

public static class NavigationManager
{
    public static void NavigateTo(Type pageType, params object?[] args)
    {
        if (Activator.CreateInstance(pageType, args) is not UserControl instance)
            throw new InvalidOperationException($"Failed to create an instance of {pageType.FullName}");

        Layout.Instance.NavigateToPage(instance);
    }

    public static void NavigateTo<T>(params object?[] args)
        where T : UserControlBase => NavigateTo(typeof(T), args);
}
