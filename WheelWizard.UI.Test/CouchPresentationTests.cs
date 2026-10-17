using System.IO.Abstractions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.MiiImages.Views;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Couch;
using WheelWizard.Views.Shell.Couch.Input;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.UI.Test;

public class CouchPresentationTests
{
    [AvaloniaFact]
    public void SwitchingViews_PreservesUnsavedPageAndRestoresDesktopGeometry()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            var navigation = services.GetRequiredService<INavigationService>();
            navigation.NavigateTo<UnsavedEditor>();
            var page = Assert.IsType<UnsavedEditor>(navigation.CurrentPage);
            page.Field.Text = "unsaved controller configuration";
            page.Field.Focus(NavigationMethod.Tab);
            var size = new Size(layout.Width, layout.Height);
            var position = layout.Position;

            layout.TogglePresentation();
            layout.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(layout.IsCouchMode);
            Assert.True(services.GetRequiredService<ISettingsManager>().COUCH_VIEW.Get());
            Assert.Same(page, navigation.CurrentPage);
            Assert.Same(layout, TopLevel.GetTopLevel(page));
            Assert.Equal("unsaved controller configuration", page.Field.Text);
            Assert.Equal(0, page.LeaveQuestions);
            Assert.True(layout.CanResize);
            Assert.True(layout.CanMaximize);
            Assert.True(layout.FindControl<CouchView>("CouchPresentation")!.IsVisible);
            Assert.False(layout.FindControl<Grid>("CompleteGrid")!.IsVisible);

            layout.Width = 1100;
            layout.Height = 700;
            layout.TogglePresentation();
            layout.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(layout.IsCouchMode);
            Assert.False(layout.CanResize);
            Assert.False(layout.CanMaximize);
            Assert.Equal(size, new Size(layout.Width, layout.Height));
            Assert.Equal(position, layout.Position);
            Assert.Same(page, navigation.CurrentPage);
            Assert.Equal("unsaved controller configuration", page.Field.Text);
            Assert.Equal(0, page.LeaveQuestions);
            Assert.True(layout.FindControl<Grid>("CompleteGrid")!.IsVisible);
        }
        finally
        {
            // Closing the fixture should not prompt about the intentionally unsaved test page.
            services.GetRequiredService<INavigationService>().NavigateAway(typeof(UserControl));
            layout.Close();
        }
    }

    [AvaloniaFact]
    public void OpenModal_PreventsModeSwitchAndBackDismissesOnlyTheModal()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            layout.TogglePresentation();
            var page = services.GetRequiredService<INavigationService>().CurrentPage;
            var question = new YesNoWindow().SetMainText("Keep changes?");
            question.Show();
            layout.TogglePresentation();
            Assert.True(layout.IsCouchMode);
            Assert.Same(question, layout.Modals.TopModal);
            Assert.False(layout.CompleteContentEnabled);
            layout.GoCouchBack();
            Dispatcher.UIThread.RunJobs();
            Assert.False(layout.Modals.HasModals);
            Assert.Same(page, services.GetRequiredService<INavigationService>().CurrentPage);
            Assert.True(layout.CompleteContentEnabled);
        }
        finally
        {
            layout.Close();
        }
    }

    [AvaloniaFact]
    public void OptionsMenuQuit_PreservesUnsavedWorkUntilLeavingIsConfirmed()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        var navigation = services.GetRequiredService<INavigationService>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            layout.TogglePresentation();
            navigation.NavigateTo<UnsavedEditor>();
            var editor = Assert.IsType<UnsavedEditor>(navigation.CurrentPage);
            editor.Field.Text = "Keep this edit";
            QuitFromMenu();
            Assert.True(layout.IsVisible);
            Assert.False(layout.Modals.HasModals);
            Assert.Same(editor, navigation.CurrentPage);
            Assert.Equal("Keep this edit", editor.Field.Text);
            Assert.Equal(1, editor.LeaveQuestions);

            editor.AllowLeave = true;
            QuitFromMenu();
            Assert.False(layout.IsVisible);
            Assert.Equal(2, editor.LeaveQuestions);
        }
        finally
        {
            navigation.NavigateAway(typeof(UserControl));
            layout.Close();
        }

        void QuitFromMenu()
        {
            layout.KeyPress(Key.F10, RawInputModifiers.None, PhysicalKey.None, "");
            layout.KeyRelease(Key.F10, RawInputModifiers.None, PhysicalKey.None, "");
            Dispatcher.UIThread.RunJobs();
            layout.UpdateLayout();
            var menu = Assert.IsType<CouchMenu>(layout.Modals.TopModal);
            var quit = menu.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "QuitLauncherButton");
            quit.Focus(NavigationMethod.Directional);
            CouchNativeInput.SendKey(quit, Key.Space);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SettingsRetainTheirSelectedPageAndReactToChangesAfterSwitchingViews()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            var navigation = services.GetRequiredService<INavigationService>();
            navigation.NavigateTo<WheelWizard.Settings.Views.SettingsPage>(typeof(WheelWizard.Settings.Views.AppearanceSettings));
            var settingsPage = Assert.IsType<WheelWizard.Settings.Views.SettingsPage>(navigation.CurrentPage);
            var content = settingsPage.FindControl<ContentControl>("SettingsContent")!;
            var selectedPage = content.Content;
            layout.UpdateLayout();
            var toggle = ((Control)selectedPage!).FindControl<WheelWizard.Views.Components.ToggleCheckBox>("CouchViewToggle")!;
            toggle.Focus(NavigationMethod.Tab);
            layout.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            layout.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(layout.IsCouchMode);
            Assert.True(toggle.IsChecked);
            toggle.Focus(NavigationMethod.Tab);
            layout.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            layout.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.False(layout.IsCouchMode);
            Assert.False(toggle.IsChecked);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(settingsPage, navigation.CurrentPage);
            Assert.Same(selectedPage, content.Content);
            var settings = services.GetRequiredService<ISettingsManager>();
            settings.ENABLE_RECOMP.Set(true, skipSave: true);
            Assert.Equal(settings.IsRecompModeActive(), settingsPage.FindControl<Control>("RecompSettingsTab")!.IsVisible);
            Assert.Equal(!settings.IsRecompModeActive(), settingsPage.FindControl<Control>("VideoSettingsTab")!.IsVisible);
        }
        finally
        {
            layout.Close();
        }
    }

    [AvaloniaFact]
    public void CouchScalePreview_RevertsAndAppliesWithoutResizingTheWindow()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        try
        {
            layout.Show();
            Pump();
            layout.TogglePresentation();
            var navigation = services.GetRequiredService<INavigationService>();
            navigation.NavigateTo<WheelWizard.Settings.Views.SettingsPage>(typeof(WheelWizard.Settings.Views.AppearanceSettings));
            Pump();
            var page = Assert.IsType<WheelWizard.Settings.Views.SettingsPage>(navigation.CurrentPage);
            var appearance = (Control)page.FindControl<ContentControl>("SettingsContent")!.Content!;
            var scale = appearance.FindControl<WheelWizard.Views.Components.Dropdown>("WindowScaleDropdown")!;
            var settings = services.GetRequiredService<ISettingsManager>();
            var size = layout.ClientSize;
            var savedScale = settings.SAVED_WINDOW_SCALE.Get();

            scale.SelectedItem = "140%";
            Pump();
            Assert.Equal(1.4, page.TransformToVisual(layout)!.Value.M11, 5);
            Assert.Equal(size, layout.ClientSize);
            var prompt = Assert.IsType<YesNoWindow>(layout.Modals.TopModal);
            Assert.Equal(1.4, prompt.TransformToVisual(layout)!.Value.M11, 5);
            prompt.FindControl<Button>("NoButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Assert.Equal(savedScale, settings.WINDOW_SCALE.Get());
            Assert.Equal(savedScale, settings.SAVED_WINDOW_SCALE.Get());
            Assert.Equal(savedScale, page.TransformToVisual(layout)!.Value.M11, 5);
            Assert.Equal(size, layout.ClientSize);

            scale.SelectedItem = "150%";
            Pump();
            prompt = Assert.IsType<YesNoWindow>(layout.Modals.TopModal);
            prompt.FindControl<Button>("YesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Assert.Equal(1.5, settings.SAVED_WINDOW_SCALE.Get());
            Assert.Equal(1.5, page.TransformToVisual(layout)!.Value.M11, 5);
            Assert.Equal(size, layout.ClientSize);
            layout.TogglePresentation();
            Pump();
            layout.TogglePresentation();
            Pump();
            Assert.Same(page, navigation.CurrentPage);
            Assert.Equal(1.5, page.TransformToVisual(layout)!.Value.M11, 5);
            Assert.Equal(size, layout.ClientSize);
        }
        finally
        {
            layout.Close();
        }

        void Pump()
        {
            Dispatcher.UIThread.RunJobs();
            layout.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void StartupInCouchView_PreservesScaleAboveTheDesktopScreenLimit()
    {
        using var services = CreateServices();
        var settings = services.GetRequiredService<ISettingsManager>();
        settings.SAVED_WINDOW_SCALE.Set(2.0, skipSave: true);
        settings.COUCH_VIEW.Set(true, skipSave: true);
        var layout = services.GetRequiredService<Layout>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            layout.UpdateLayout();
            Assert.True(layout.IsCouchMode);
            Assert.Equal(2.0, settings.SAVED_WINDOW_SCALE.Get());
            var page = services.GetRequiredService<INavigationService>().CurrentPage!;
            Assert.Equal(2.0, page.TransformToVisual(layout)!.Value.M11, 5);
        }
        finally
        {
            layout.Close();
        }
    }

    [AvaloniaFact]
    public void ScaledKeyboard_KeepsBothEdgesOfTheKeyGridInsideTheWindow()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            layout.TogglePresentation();
            layout.Width = 1280;
            layout.Height = 720;
            services.GetRequiredService<ISettingsManager>().WINDOW_SCALE.Set(1.5, skipSave: true);
            var dialog = new TextInputWindow().SetInitialText("Steam Deck");
            dialog.SetModalOwner(layout).Show();
            layout.UpdateLayout();
            var keyboard = new CouchKeyboard(dialog.FindControl<WheelWizard.Views.Components.InputField>("InputField")!, () => { });
            keyboard.SetModalOwner(dialog).Show();
            Dispatcher.UIThread.RunJobs();
            layout.UpdateLayout();
            var keys = keyboard.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single(grid => grid.Rows == 5);
            foreach (var key in new[] { keys.Children.First(), keys.Children.Last() })
            {
                var rectangle = new Rect(key.Bounds.Size).TransformToAABB(key.TransformToVisual(layout)!.Value);
                Assert.True(rectangle.Left >= 0, $"Left edge is clipped: {rectangle}.");
                Assert.True(rectangle.Right <= layout.ClientSize.Width, $"Right edge is clipped: {rectangle}.");
            }
        }
        finally
        {
            layout.Close();
        }
    }

    [AvaloniaFact]
    public void NavigatingAwayInCouchMode_LeavesEveryPageUsableWhenReturningToDesktop()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            var navigation = services.GetRequiredService<INavigationService>();
            Type[] pages =
            [
                typeof(WheelWizard.RrRooms.Views.RoomsPage),
                typeof(WheelWizard.WheelWizardData.Views.FriendsPage),
                typeof(WheelWizard.WheelWizardData.Views.LeaderboardPage),
                typeof(WheelWizard.WheelWizardData.Views.UserProfilePage),
                typeof(WheelWizard.WiiManagement.MiiManagement.Views.MiiListPage),
            ];
            foreach (var type in pages)
            {
                navigation.NavigateTo(type);
                layout.UpdateLayout();
                var desktopViewport = navigation.CurrentPage!.Bounds.Size;
                layout.TogglePresentation();
                navigation.NavigateTo<WheelWizard.Views.Shell.Views.HomePage>();
                navigation.NavigateTo(type);
                var returningPage = navigation.CurrentPage;
                layout.TogglePresentation();
                layout.UpdateLayout();
                Assert.Same(returningPage, navigation.CurrentPage);
                Assert.Equal(desktopViewport, returningPage!.Bounds.Size);
                Assert.Same(layout, TopLevel.GetTopLevel(returningPage));
                Assert.True(layout.CompleteContentEnabled);
            }
        }
        finally
        {
            layout.Close();
        }
    }

    [AvaloniaFact]
    public void EditorControlTraversal_DoesNotModifyTheSelectedMiiPart()
    {
        using var services = CreateServices();
        var layout = services.GetRequiredService<Layout>();
        var navigation = services.GetRequiredService<INavigationService>();
        try
        {
            layout.Show();
            Dispatcher.UIThread.RunJobs();
            layout.TogglePresentation();
            navigation.NavigateTo<WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorPage>(
                new WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorRequest(
                    WheelWizard.WiiManagement.MiiManagement.MiiFactory.CreateDefaultMale()
                )
            );
            layout.UpdateLayout();
            var editor = (WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorPage)navigation.CurrentPage!;
            var sidebar = editor.FindControl<StackPanel>("SidebarItems")!;
            sidebar
                .Children.OfType<Button>()
                .Single(button => Equals(button.Content, "Head"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sidebar
                .Children.OfType<Button>()
                .First(button => !Equals(button.Content, "Back"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            layout.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var first = sidebar.Children.OfType<Button>().First(button => !Equals(button.Content, "Back"));
            first.Focus(NavigationMethod.Directional);
            layout.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
            layout.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
            Assert.False(editor.HasUnsavedWork);
            Assert.NotSame(first, layout.FocusManager!.GetFocusedElement());
        }
        finally
        {
            navigation.NavigateAway(typeof(UserControl));
            layout.Close();
        }
    }

    private static ServiceProvider CreateServices()
    {
        var fileSystem = new MockFileSystem();
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-couch-tests");
        fileSystem.Directory.CreateDirectory(directory);
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(directory);
        var registrations = new ServiceCollection();
        registrations.AddWheelWizardServices(location);
        registrations.AddSingleton<IFileSystem>(fileSystem);
        registrations.AddTransient(typeof(IApiCaller<>), typeof(ApplicationCompositionTests.OfflineApiCaller<>));
        var services = registrations.BuildServiceProvider();
        services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
        var settings = services.GetRequiredService<ISettingsManager>();
        settings.ENABLE_ANIMATIONS.Set(false, skipSave: true);
        settings.NAND_ROOT_PATH.Set(directory, skipSave: true);
        settings.LOAD_PATH.Set(directory, skipSave: true);
        services.GetRequiredService<MiiControlThemes>().Install(Application.Current!.Resources);
        services.GetRequiredService<WindowAppearance>().Install(Application.Current.Resources);
        return services;
    }

    public sealed class UnsavedEditor : UserControl, INavigationGuard
    {
        public TextBox Field { get; } = new();
        public int LeaveQuestions { get; private set; }
        public bool AllowLeave { get; set; }
        public bool HasUnsavedWork => true;

        public UnsavedEditor() => Content = Field;

        public Task<bool> ConfirmLeaveAsync()
        {
            LeaveQuestions++;
            return Task.FromResult(AllowLeave);
        }
    }
}
