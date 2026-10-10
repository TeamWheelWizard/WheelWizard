using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.MiiImages.Views;
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Views.Editor;
using Button = Avalonia.Controls.Button;

namespace WheelWizard.UI.Test;

public class MiiEditorPageTests
{
    [AvaloniaFact]
    public async Task NewMii_PickWithDice_ThenWalksEveryMenuLevel_AndTracksUnsavedWork()
    {
        using var services = BuildServices();
        var settings = services.GetRequiredService<ISettingsManager>();
        settings.ENABLE_ANIMATIONS.Set(false, skipSave: true);
        var page = services.GetRequiredService<IPageFactory>().Create<MiiEditorPage>(new MiiEditorRequest(null));
        var window = new Window
        {
            Content = page,
            Width = 580,
            Height = 820,
        };
        try
        {
            window.Show();
            window.UpdateLayout();

            var headerDivider = page.FindControl<Border>("EditorHeaderDivider")!;
            var bottomDivider = page.FindControl<Border>("EditorBottomDivider")!;
            Assert.True(headerDivider.IsVisible);
            Assert.True(bottomDivider.IsVisible);
            Assert.Equal(1, headerDivider.Bounds.Height);
            Assert.Equal(1, bottomDivider.Bounds.Height);
            Assert.Equal(page.Bounds.Width, headerDivider.Bounds.Width);
            Assert.Equal(page.Bounds.Width, bottomDivider.Bounds.Width);

            // Picker first: no editor chrome yet.
            Assert.True(page.FindControl<StackPanel>("PickerHint")!.IsVisible);
            var dice = page.FindControl<Avalonia.Controls.Button>("DiceButton")!;
            Assert.True(dice.IsVisible);
            Assert.Equal(0, page.FindControl<Border>("SidebarPanel")!.Opacity);
            Assert.False(page.HasUnsavedWork);

            dice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => !dice.IsVisible);
            window.UpdateLayout();

            // Overview: Head, Body, Info.
            Assert.True(headerDivider.IsVisible);
            Assert.True(bottomDivider.IsVisible);
            var sidebar = page.FindControl<StackPanel>("SidebarItems")!;
            Assert.True(page.FindControl<Border>("SidebarPanel")!.IsHitTestVisible);
            Assert.Equal(3, Buttons(sidebar).Count);
            Assert.False(page.HasUnsavedWork);

            // Head: back + the six groups.
            Click(Buttons(sidebar)[0]);
            Assert.Equal(7, Buttons(sidebar).Count);

            // Eyes group: back + eyes, brows, glasses; eyes selected with its variants, colours and tools.
            Click(Buttons(sidebar)[3]);
            Assert.Equal(4, Buttons(sidebar).Count);
            Assert.True(Buttons(sidebar)[1].IsChecked);
            Assert.Equal(48, page.FindControl<StackPanel>("VariantItems")!.Children.Count);
            Assert.Equal(4, page.FindControl<StackPanel>("ToolItems")!.Children.Count);
            Assert.False(page.FindControl<Border>("ColorPanel")!.Classes.Contains("hidden"));

            // Cycling makes it unsaved; undoing makes it clean again.
            page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Assert.True(page.HasUnsavedWork);
            Assert.True(page.FindControl<Button>("UndoButton")!.IsEnabled);
            page.RaiseEvent(
                new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Z,
                    KeyModifiers = KeyModifiers.Control,
                }
            );
            Assert.False(page.HasUnsavedWork);
            page.RaiseEvent(
                new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Y,
                    KeyModifiers = KeyModifiers.Control,
                }
            );
            Assert.True(page.HasUnsavedWork);

            // Glasses: types without "none" (the +/- button adds and removes them); no size tools while there are none.
            Click(Buttons(sidebar)[3]);
            Assert.Equal(8, page.FindControl<StackPanel>("VariantItems")!.Children.Count);
            Assert.Empty(page.FindControl<StackPanel>("ToolItems")!.Children);

            // Picking a glasses type puts them on.
            Click((ToggleButton)page.FindControl<StackPanel>("VariantItems")!.Children[2]);
            Assert.NotEmpty(page.FindControl<StackPanel>("ToolItems")!.Children);

            // Escape walks back out: parts → groups → whole Mii.
            page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(7, Buttons(sidebar).Count);
            page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(3, Buttons(sidebar).Count);

            // Body: gender buttons, sliders and favourite colours.
            Click(Buttons(sidebar)[1]);
            Assert.Equal(5, Buttons(sidebar).Count);
            Assert.False(page.FindControl<Border>("BodyPanel")!.Classes.Contains("hidden"));
            Assert.Equal(12, page.FindControl<StackPanel>("SwatchItems")!.Children.Count);

            // Info: the name card.
            Click(Buttons(sidebar)[2]);
            Assert.False(page.FindControl<Border>("InfoCard")!.Classes.Contains("hidden"));
            Assert.True(page.FindControl<Border>("BodyPanel")!.Classes.Contains("hidden"));
            Assert.False(string.IsNullOrWhiteSpace(page.FindControl<TextField>("NameField")!.Text));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LeavingWithUnsavedWork_AsksFirst_AndStaysWhenRefused()
    {
        var pages = Substitute.For<IPageFactory>();
        var guarded = new GuardedPage { HasUnsavedWork = true };
        var other = new UserControl();
        pages.Create(typeof(GuardedPage), Arg.Any<object[]>()).Returns(guarded);
        pages.Create(typeof(UserControl), Arg.Any<object[]>()).Returns(other);
        var navigation = new NavigationService(pages);
        navigation.NavigateTo(typeof(GuardedPage));
        Assert.Same(guarded, navigation.CurrentPage);

        guarded.Answer = false;
        navigation.NavigateTo(typeof(UserControl));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Same(guarded, navigation.CurrentPage);
        Assert.Equal(1, guarded.Asked);

        guarded.Answer = true;
        navigation.NavigateTo(typeof(UserControl));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Same(other, navigation.CurrentPage);

        // Nothing unsaved: leaves without asking.
        navigation.NavigateTo(typeof(GuardedPage));
        guarded.HasUnsavedWork = false;
        navigation.NavigateTo(typeof(UserControl));
        Assert.Same(other, navigation.CurrentPage);
        Assert.Equal(2, guarded.Asked);
    }

    private sealed class GuardedPage : UserControl, INavigationGuard
    {
        public bool HasUnsavedWork { get; set; }
        public bool Answer { get; set; }
        public int Asked { get; private set; }

        public Task<bool> ConfirmLeaveAsync()
        {
            Asked++;
            return Task.FromResult(Answer);
        }
    }

    private static List<MultiIconRadioButton> Buttons(StackPanel sidebar) => sidebar.Children.OfType<MultiIconRadioButton>().ToList();

    private static void Click(Avalonia.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static ServiceProvider BuildServices()
    {
        var fileSystem = new MockFileSystem();
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-ui-mii-editor");
        fileSystem.Directory.CreateDirectory(directory);
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(directory);
        var registrations = new ServiceCollection();
        registrations.AddWheelWizardServices(location);
        registrations.AddSingleton<System.IO.Abstractions.IFileSystem>(fileSystem);
        registrations.AddTransient(typeof(IApiCaller<>), typeof(ApplicationCompositionTests.OfflineApiCaller<>));
        registrations.AddSingleton(Substitute.For<IModManager>());
        registrations.AddSingleton(Substitute.For<IMiiDbService>());
        var services = registrations.BuildServiceProvider();
        services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
        services.GetRequiredService<MiiControlThemes>().Install(Application.Current!.Resources);
        return services;
    }
}
