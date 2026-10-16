using System.IO.Abstractions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Couch;
using WheelWizard.Views.Shell.Couch.Input;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using WheelWizard.WiiManagement.MiiManagement.Views;
using WheelWizard.WiiManagement.MiiManagement.Views.Editor;

namespace WheelWizard.UI.Test;

public class MiiEditorCouchNavigationTests
{
    [AvaloniaFact]
    public void MetadataLabels_DoNotTrapDirectionalFocusBetweenFieldsAndColor()
    {
        using var fixture = new EditorFixture(MiiFactory.CreateDefaultMale());
        fixture.Activate(fixture.Side("attribute.mii_section.info"));
        var name = fixture.Page.FindControl<InputField>("NameField")!;
        var creator = fixture.Page.FindControl<InputField>("CreatorField")!;
        var color = fixture.Page.FindControl<Button>("ColorButton")!;
        fixture.MoveTo(name);
        Assert.True(CouchInputController.MoveFocus(fixture.Window, name, NavigationDirection.Down));
        Assert.Same(creator, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.True(CouchInputController.MoveFocus(fixture.Window, creator, NavigationDirection.Down));
        Assert.Same(color, fixture.Window.FocusManager.GetFocusedElement());
    }

    [AvaloniaFact]
    public void Creation_TraversesEveryHeadGroupAndEditsNativeControlsBeforeSaving()
    {
        using var fixture = new EditorFixture(null);
        var page = fixture.Page;
        Assert.Same(page.FindControl<ActionButton>("PickBoyButton"), page.CouchFocusTarget);
        Assert.Same(page.CouchFocusTarget, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.False(page.TryGoBack());
        fixture.Activate(page.FindControl<ActionButton>("PickGirlButton")!);
        Assert.False(page.Focusable);
        Assert.False(page.HasUnsavedWork);

        fixture.Activate(fixture.Side("attribute.mii_section.head"));
        var firstGroup = page.CouchFocusTarget;
        fixture.Key(Key.Right);
        Assert.False(page.HasUnsavedWork);
        Assert.NotSame(firstGroup, fixture.Window.FocusManager.GetFocusedElement());

        foreach (var group in MiiEditorParts.Groups)
        {
            fixture.Activate(fixture.Side(MiiEditorParts.GroupTitleKey(group)));
            foreach (var definition in MiiEditorParts.PartsOf(group))
            {
                if (MiiEditorParts.PartsOf(group).Count > 1)
                    fixture.Activate(fixture.Side(definition.TitleKey));
                Assert.IsAssignableFrom<Button>(page.CouchFocusTarget);
                if (definition.Presence is not null)
                {
                    var presence = fixture
                        .Panel("CouchPartActions")
                        .Children.OfType<ActionButton>()
                        .Single(button => Equals(button.Tag, definition.Part));
                    fixture.Activate(presence);
                    Assert.Equal(definition.Part, Assert.IsType<ActionButton>(fixture.Window.FocusManager.GetFocusedElement()).Tag);
                }
                if (definition.Variants is not null)
                {
                    var variant = fixture.Panel("VariantItems").Children.OfType<ToggleButton>().First(button => button.IsChecked != true);
                    fixture.Activate(variant);
                    Assert.True(variant.IsChecked);
                    var focusedVariant = fixture.Window.FocusManager.GetFocusedElement() as Control;
                    Assert.True(
                        ReferenceEquals(variant, focusedVariant),
                        $"{definition.Part}: expected variant {variant.Tag} in {variant.Parent?.GetType().Name}; actual {focusedVariant?.GetType().Name}:{focusedVariant?.Tag} in {focusedVariant?.Parent?.GetType().Name}; variant panel opacity={page.FindControl<Border>("VariantPanel")!.Opacity}."
                    );
                }
                // Optional parts may have just been removed. Only visible, enabled edit actions are used.
                foreach (
                    var tag in fixture
                        .Panel("ToolItems")
                        .Children.Where(control => control.IsEnabled)
                        .Select(control => control.Tag)
                        .ToArray()
                )
                {
                    var tool = fixture.Panel("ToolItems").Children.FirstOrDefault(control => Equals(control.Tag, tag) && control.IsEnabled);
                    if (tool is null)
                        continue;
                    fixture.Activate(tool);
                    Assert.IsAssignableFrom<Button>(fixture.Window.FocusManager.GetFocusedElement());
                    Assert.NotSame(page, fixture.Window.FocusManager.GetFocusedElement());
                }
                if (definition.DragVertical is not null)
                {
                    var movement = fixture
                        .Panel("CouchPartActions")
                        .Children.OfType<ActionButton>()
                        .First(button =>
                            button.IsEnabled && button.Tag is string key && key.StartsWith("vertical-", StringComparison.Ordinal)
                        );
                    fixture.Activate(movement);
                    Assert.IsType<ActionButton>(fixture.Window.FocusManager.GetFocusedElement());
                }
                if (definition.Colors is not null)
                {
                    fixture.Activate(page.FindControl<Button>("ColorButton")!);
                    var swatch = fixture
                        .Panel("SwatchItems")
                        .Children.OfType<Button>()
                        .First(button => !button.Classes.Contains("selected"));
                    fixture.Activate(swatch);
                    Assert.Contains("selected", swatch.Classes);
                    Assert.False(page.FindControl<ScrollViewer>("SwatchScroller")!.IsVisible);
                    Assert.Same(page.FindControl<Button>("ColorButton"), fixture.Window.FocusManager.GetFocusedElement());
                }
            }
            fixture.Key(Key.Escape);
            Assert.NotNull(fixture.Side(MiiEditorParts.GroupTitleKey(group)));
        }
        fixture.Key(Key.Escape);
        fixture.Activate(fixture.Side("attribute.mii_section.info"));
        var name = page.FindControl<InputField>("NameField")!;
        fixture.MoveTo(name);
        fixture.Key(Key.A, RawInputModifiers.Control);
        fixture.Window.KeyTextInput("Controller");
        fixture.Pump();
        Assert.True(page.HasUnsavedWork);
        var save = page.FindControl<StackPanel>("CouchEditorActions")!.Children.OfType<ActionButton>().Last();
        fixture.Activate(save);
        Assert.NotNull(fixture.SavedMii);
        Assert.True(fixture.SavedMii!.IsGirl);
        Assert.IsType<MiiListPage>(fixture.Navigation.CurrentPage);
    }

    [AvaloniaFact]
    public void ColorsAndSections_BackClosesOnlyOneLevelAndPreservesHistoryAcrossPresentationChanges()
    {
        using var fixture = new EditorFixture(MiiFactory.CreateDefaultMale());
        var page = fixture.Page;
        fixture.Activate(fixture.Side("attribute.mii_section.info"));
        var name = page.FindControl<InputField>("NameField")!;
        fixture.MoveTo(name);
        fixture.Key(Key.A, RawInputModifiers.Control);
        fixture.Window.KeyTextInput("Couch Mii");
        fixture.Pump();
        Assert.Equal("Couch Mii", name.Text);
        fixture.Activate(page.FindControl<Button>("ColorButton")!);
        Assert.Same(page.CouchFocusTarget, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.True(page.FindControl<ScrollViewer>("SwatchScroller")!.IsVisible);
        fixture.Key(Key.Escape);
        Assert.False(page.FindControl<ScrollViewer>("SwatchScroller")!.IsVisible);
        Assert.Same(page.FindControl<Button>("ColorButton"), fixture.Window.FocusManager.GetFocusedElement());
        Assert.True(page.FindControl<Border>("InfoCard")!.IsVisible);
        fixture.Key(Key.Escape);
        Assert.False(page.FindControl<Border>("InfoCard")!.IsVisible);
        Assert.False(page.TryGoBack());
        Assert.Equal("Couch Mii", name.Text);

        fixture.Window.TogglePresentation();
        fixture.Pump();
        fixture.Window.TogglePresentation();
        fixture.Pump();
        Assert.Equal("Couch Mii", name.Text);
        Assert.IsAssignableFrom<Button>(page.CouchFocusTarget);
        var actions = page.FindControl<StackPanel>("CouchEditorActions")!.Children.OfType<ActionButton>().ToArray();
        fixture.Activate(actions[0]);
        Assert.NotEqual("Couch Mii", name.Text);
        fixture.Activate(actions[1]);
        Assert.Equal("Couch Mii", name.Text);

        fixture.Activate(fixture.Side("attribute.mii_section.body"));
        var height = page.FindControl<ValueSlider>("HeightSlider")!;
        fixture.MoveTo(height);
        var previous = height.Value;
        CouchNativeInput.SendKey(height, previous < height.Maximum ? Key.Up : Key.Down);
        fixture.Pump();
        Assert.NotEqual(previous, height.Value);
        Assert.Same(height, fixture.Window.FocusManager!.GetFocusedElement());
        fixture.Key(Key.Escape);
        Assert.False(page.FindControl<Border>("BodyPanel")!.IsVisible);
        Assert.IsAssignableFrom<Button>(fixture.Window.FocusManager.GetFocusedElement());
    }

    [AvaloniaFact]
    public void OuterBack_UsesUnsavedGuardAndKeepEditingRetainsThePage()
    {
        using var fixture = new EditorFixture(MiiFactory.CreateDefaultMale());
        fixture.Activate(fixture.Side("attribute.mii_section.info"));
        var name = fixture.Page.FindControl<InputField>("NameField")!;
        fixture.MoveTo(name);
        fixture.Key(Key.A, RawInputModifiers.Control);
        fixture.Window.KeyTextInput("Keep Me");
        fixture.Key(Key.Escape);
        Assert.False(fixture.Page.TryGoBack());
        fixture.Key(Key.Escape);
        Assert.True(fixture.Window.Modals.HasModals);
        Assert.Same(fixture.Page, fixture.Navigation.CurrentPage);
        var keep = fixture.Window.Modals.TopModal!.FindControl<ActionButton>("NoButton")!;
        fixture.Activate(keep);
        Assert.False(fixture.Window.Modals.HasModals);
        Assert.Same(fixture.Page, fixture.Navigation.CurrentPage);
        Assert.Equal("Keep Me", name.Text);
        Assert.True(fixture.Page.HasUnsavedWork);
    }

    private sealed class EditorFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public Layout Window { get; }
        public INavigationService Navigation { get; }
        public MiiEditorPage Page { get; }
        public Mii? SavedMii { get; private set; }

        public EditorFixture(Mii? mii)
        {
            var fileSystem = new MockFileSystem();
            var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-editor-navigation-tests");
            fileSystem.Directory.CreateDirectory(directory);
            var location = Substitute.For<IApplicationDataLocation>();
            location.DirectoryPath.Returns(directory);
            var registrations = new ServiceCollection();
            registrations.AddWheelWizardServices(location);
            registrations.AddSingleton<IFileSystem>(fileSystem);
            registrations.AddTransient(typeof(IApiCaller<>), typeof(ApplicationCompositionTests.OfflineApiCaller<>));
            var database = Substitute.For<IMiiDbService>();
            database.Exists().Returns(true);
            database.GetAllMiis().Returns([]);
            database.AddToDatabase(Arg.Do<Mii?>(value => SavedMii = value), Arg.Any<string>()).Returns(OperationResult.Ok());
            database.Update(Arg.Do<Mii>(value => SavedMii = value)).Returns(OperationResult.Ok());
            registrations.AddSingleton(database);
            _services = registrations.BuildServiceProvider();
            _services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
            var settings = _services.GetRequiredService<ISettingsManager>();
            settings.ENABLE_ANIMATIONS.Set(false, skipSave: true);
            settings.NAND_ROOT_PATH.Set(directory, skipSave: true);
            settings.LOAD_PATH.Set(directory, skipSave: true);
            _services.GetRequiredService<MiiControlThemes>().Install(Application.Current!.Resources);
            _services.GetRequiredService<WindowAppearance>().Install(Application.Current.Resources);
            Window = _services.GetRequiredService<Layout>();
            Window.Show();
            try
            {
                Pump();
                Window.TogglePresentation();
                Navigation = _services.GetRequiredService<INavigationService>();
                if (mii is null)
                {
                    Navigation.NavigateTo<MiiListPage>();
                    Pump();
                    var list = Assert.IsType<MiiListPage>(Navigation.CurrentPage);
                    Activate(list.GetVisualDescendants().OfType<MiiBlock>().Single(block => block.Mii is null));
                }
                else
                    Navigation.NavigateTo<MiiEditorPage>(new MiiEditorRequest(mii));
                Page = Assert.IsType<MiiEditorPage>(Navigation.CurrentPage);
                Pump();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Panel Panel(string name) => Page.FindControl<Panel>(name)!;

        public Button Side(string key) => Panel("SidebarItems").Children.OfType<Button>().Single(button => Equals(button.Tag, key));

        public void Pump()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPress(key, modifiers, PhysicalKey.None, "");
            Window.KeyRelease(key, modifiers, PhysicalKey.None, "");
            Pump();
        }

        // Native Tab traversal crosses rebuilds without forcing focus onto every requested control.
        public void MoveTo(Control target)
        {
            Pump();
            var visited = new HashSet<string>();
            for (var attempt = 0; attempt < 300; attempt++)
            {
                if (ReferenceEquals(target, Window.FocusManager!.GetFocusedElement()))
                    return;
                if (Window.FocusManager!.GetFocusedElement() is Control focused)
                    visited.Add($"{focused.GetType().Name}:{focused.Name}:{focused.Tag}");
                Key(Avalonia.Input.Key.Tab);
            }
            var ancestry = target
                .GetVisualAncestors()
                .OfType<Control>()
                .Select(control => $"{control.GetType().Name}:{KeyboardNavigation.GetTabNavigation(control)}");
            var details = string.Join(", ", ancestry);
            var traversed = string.Join(", ", visited);
            Assert.Fail(
                $"Native traversal did not reach {target.GetType().Name}:{target.Name}:{target.Tag}; focusable={target.Focusable}, tab={target.IsTabStop}, visible={target.IsEffectivelyVisible}, enabled={target.IsEffectivelyEnabled}; visited={traversed}; ancestors={details}."
            );
        }

        public void Activate(Control target)
        {
            MoveTo(target);
            Key(Avalonia.Input.Key.Space);
        }

        public void Dispose()
        {
            try
            {
                Navigation?.NavigateAway(typeof(UserControl));
                Window?.Modals.CloseAll();
                Window?.Close();
            }
            finally
            {
                _services.Dispose();
            }
        }
    }
}
