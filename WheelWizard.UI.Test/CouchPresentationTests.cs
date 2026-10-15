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
        public bool HasUnsavedWork => true;

        public UnsavedEditor() => Content = Field;

        public Task<bool> ConfirmLeaveAsync()
        {
            LeaveQuestions++;
            return Task.FromResult(false);
        }
    }
}
