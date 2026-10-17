using Avalonia.Controls;
using Avalonia.Input;
using WheelWizard.RrRooms.Views;
using WheelWizard.Settings.Views;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell.Controls;
using WheelWizard.WiiManagement.MiiManagement.Views;
using WheelWizard.WiiManagement.MiiManagement.Views.Editor;

namespace WheelWizard.Views.Shell.Couch;

public partial class CouchView : UserControl
{
    private readonly List<(Type Page, ActionButton Button)> _routes = [];
    public event Action<Type>? NavigationRequested;

    public CouchView()
    {
        InitializeComponent();
    }

    public Grid ContentHost => PageHost;

    public void SetRoutes(IEnumerable<SidebarRadioButton> routes)
    {
        _routes.Clear();
        NavigationButtons.Children.Clear();
        foreach (var route in routes.Where(route => route.IsVisible && route.PageType != null))
            AddRoute(route.PageType!, route.Text);
        AddRoute(typeof(SettingsPage), t("page_title.settings"));
    }

    private void AddRoute(Type page, string label)
    {
        var button = new ActionButton { Text = label };
        button.Classes.Add("couch-navigation");
        button.Click += (_, _) => NavigationRequested?.Invoke(page);
        _routes.Add((page, button));
        NavigationButtons.Children.Add(button);
    }

    public void SelectPage(UserControl page)
    {
        var type = page switch
        {
            RoomDetailsPage => typeof(RoomsPage),
            MiiEditorPage => typeof(MiiListPage),
            _ => page.GetType(),
        };
        foreach (var route in _routes)
            route.Button.Classes.Set("selected", route.Page == type);
    }

    public void NavigateSection(int offset)
    {
        if (_routes.Count == 0)
            return;
        var selected = _routes.FindIndex(route => route.Button.Classes.Contains("selected"));
        var index = (Math.Max(0, selected) + offset % _routes.Count + _routes.Count) % _routes.Count;
        NavigationRequested?.Invoke(_routes[index].Page);
    }

    public void FocusNavigation()
    {
        var route = _routes.FirstOrDefault(route => route.Button.Classes.Contains("selected"));
        (route.Button ?? _routes.FirstOrDefault().Button)?.Focus(NavigationMethod.Directional);
    }

    public void RefreshLabels() => InputHints.RefreshLabels();
}
