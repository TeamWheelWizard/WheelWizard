using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WheelWizard.AutoUpdating.Presentation;
using WheelWizard.Models;
using WheelWizard.RrRooms;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Controls;
using WheelWizard.WheelWizardData.ViewModels;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WheelWizardData.Views.Dialogs;

namespace WheelWizard.UI.Test;

public class WindowCompositionTests
{
    [AvaloniaTheory]
    [InlineData("TitleBar")]
    [InlineData("TitleBar, Border")]
    [InlineData("TitleBar, Border, Shadow")]
    public void LinuxCaptionButtons_FitTheCustomTitleBar(string decorationParts)
    {
        var popup = new TestWindow
        {
            Width = 656,
            Height = 876,
            ExtendClientAreaTitleBarHeightHint = 30,
        };
        try
        {
            popup.Show();
            // Exercise the pinned framework's drawn decorations under the headless backend.
            var host = typeof(TopLevel)
                .GetProperty("TopLevelHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(popup)!;
            var theme = (ControlTheme)Application.Current!.Resources["LinuxWindowDecorations"]!;
            var update = host.GetType()
                .GetMethod(
                    "UpdateDrawnDecorations",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                )!;
            var partsType = Nullable.GetUnderlyingType(update.GetParameters()[0].ParameterType)!;
            var parts = Enum.Parse(partsType, decorationParts);
            update.Invoke(host, [parts, WindowState.Normal, theme]);
            popup.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var buttons = ((Visual)host)
                .GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Name is "PART_MinimizeButton" or "PART_MaximizeButton" or "PART_CloseButton")
                .ToList();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
            {
                Assert.Equal(30, button.Bounds.Width);
                Assert.Equal(30, button.Bounds.Height);
                Assert.Equal(0, button.Margin.Top);
                var position = button.TranslatePoint(default, popup)!.Value;
                Assert.Equal(0, position.Y);
                Assert.True(position.X + button.Bounds.Width <= popup.ClientSize.Width);
            }
            var captionPanel = buttons[0].Parent as StackPanel;
            Assert.NotNull(captionPanel);
            Assert.Equal(new Thickness(0), captionPanel.Margin);
        }
        finally
        {
            popup.Close();
        }
    }

    [AvaloniaFact]
    public async Task LinuxManualUpdate_DirectsUsersToTheirPackageManager()
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(true);
        using var popup = new TestWindow { Width = 656, Height = 876 };
        popup.Show();
        await new UpdatePresentation(environment).ShowManualUpdateAsync("2.0.0", "1.0.0");
        var content = Assert.IsType<MessageBoxWindow>(popup.Modals.TopModal);
        Assert.Contains("through your package manager", content.FindControl<SelectableTextBlock>("MessageInformationBlock")!.Text);
        Assert.Equal(
            "https://github.com/TeamWheelWizard/WheelWizard/releases",
            content.FindControl<WheelWizard.Views.Components.ActionButton>("MessageLink")!.Text
        );
    }

    [AvaloniaFact]
    public void AppearanceResources_FollowTheAnimationSettingAndUnsubscribe()
    {
        var settings = Substitute.For<ISettingsManager>();
        var animations = new WhWzSetting<bool>("Animations", false);
        var scale = new WhWzSetting<double>("Scale", 1);
        settings.ENABLE_ANIMATIONS.Returns(animations);
        settings.WINDOW_SCALE.Returns(scale);
        settings.Get<double>(scale).Returns(_ => scale.Get());
        var signals = new SettingsSignalBus(NullLogger<SettingsSignalBus>.Instance);
        var resources = new ResourceDictionary();
        using var appearance = new WindowAppearance(settings, signals);
        appearance.Install(resources);
        Assert.Equal(TimeSpan.Zero, resources[WindowAppearance.ControlAnimationDurationResourceKey]);
        Assert.Equal(TimeSpan.Zero, resources[WindowAppearance.SegmentAnimationDurationResourceKey]);
        Assert.Equal(false, resources[WindowAppearance.ControlAnimationsEnabledResourceKey]);
        animations.Set(true);
        signals.Publish(animations);
        Assert.Equal(TimeSpan.FromMilliseconds(120), resources[WindowAppearance.ControlAnimationDurationResourceKey]);
        Assert.Equal(TimeSpan.FromMilliseconds(240), resources[WindowAppearance.SegmentAnimationDurationResourceKey]);
        Assert.Equal(true, resources[WindowAppearance.ControlAnimationsEnabledResourceKey]);
        animations.Set(false);
        signals.Publish(animations);
        Assert.Equal(TimeSpan.Zero, resources[WindowAppearance.ControlAnimationDurationResourceKey]);
        Assert.Equal(TimeSpan.Zero, resources[WindowAppearance.SegmentAnimationDurationResourceKey]);
        Assert.Equal(false, resources[WindowAppearance.ControlAnimationsEnabledResourceKey]);
        appearance.Dispose();
        animations.Set(true);
        signals.Publish(animations);
        Assert.Equal(TimeSpan.Zero, resources[WindowAppearance.ControlAnimationDurationResourceKey]);
        Assert.Equal(TimeSpan.Zero, resources[WindowAppearance.SegmentAnimationDurationResourceKey]);
    }

    [AvaloniaFact]
    public void SidebarLabels_SurviveRepeatedCollapseAndReattachment()
    {
        var button = new WheelWizard.Views.Shell.Controls.SidebarRadioButton { Text = "My Miis" };
        var host = new Border { Child = button, Width = 221 };
        using var window = new UiTestWindow
        {
            Content = host,
            Width = 221,
            Height = 46,
        };
        window.Show();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            button.Classes.Set("compact", true);
            host.Width = 64;
            window.UpdateLayout();
            Assert.Equal(64, button.Bounds.Width);
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Label");
            Assert.Equal(0, label.Opacity);
            host.Child = null;
            host.Child = button;
            button.Text = $"My Miis {cycle}";
            button.Classes.Set("compact", false);
            host.Width = 221;
            window.UpdateLayout();
            label = button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Label");
            Assert.Equal(1, label.Opacity);
            Assert.Equal(button.Text, label.Text);
        }
    }

    [AvaloniaFact]
    public void SettingsContent_StaysStationaryWhileNavigationIsRevealed()
    {
        var pages = Substitute.For<WheelWizard.Views.Shell.Navigation.IPageFactory>();
        pages.Create(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(_ => new UserControl());
        var page = new WheelWizard.Settings.Views.SettingsPage(
            Substitute.For<IModalFactory>(),
            Substitute.For<ISettingsManager>(),
            new SettingsSignalBus(NullLogger<SettingsSignalBus>.Instance),
            pages
        );
        page.UpdateLayoutWidth(572);
        var host = new Border
        {
            Child = page,
            Width = 415,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        using var window = new UiTestWindow
        {
            Content = host,
            Width = 572,
            Height = 700,
        };
        window.Show();
        window.UpdateLayout();
        var content = page.FindControl<ContentControl>("SettingsContent")!;
        var position = content.TranslatePoint(default, window);
        var size = content.Bounds.Size;
        foreach (var width in new[] { 415.0, 430.0, 480.0, 540.0, 572.0 })
        {
            host.Width = width;
            window.UpdateLayout();
            Assert.Equal(position, content.TranslatePoint(default, window));
            Assert.Equal(size, content.Bounds.Size);
        }
    }

    [AvaloniaFact]
    public async Task ProgressDismissal_DoesNotCancelButExplicitCancelDoes()
    {
        using var cancellation = new CancellationTokenSource();
        using var owner = new TestWindow { Width = 656, Height = 876 };
        owner.Show();
        var content = new ProgressWindow("Download").SetCancellationTokenSource(cancellation);
        var closed = content.ShowDialog();
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(owner, TopLevel.GetTopLevel(content));
        owner.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        owner.MouseDown(new Point(5, 50), Avalonia.Input.MouseButton.Left);
        owner.MouseUp(new Point(5, 50), Avalonia.Input.MouseButton.Left);
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Same(content, owner.Modals.TopModal);
        var cancel = content.FindControl<WheelWizard.Views.Components.ActionButton>("CancelButton")!;
        var point = cancel.TranslatePoint(new Point(cancel.Bounds.Width / 2, cancel.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(point, Avalonia.Input.MouseButton.Left);
        owner.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(content.WasCancellationRequested);
        Assert.Same(content, owner.Modals.TopModal);
        content.Close();
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(owner.CanInteract);
    }

    [AvaloniaFact]
    public async Task NestedModals_StayInsideTheirOwner_AndRestoreInteraction()
    {
        var first = new TestWindow { Width = 656, Height = 876 };
        var second = new TestWindow { Width = 656, Height = 876 };
        first.Show();
        second.Show();
        var popup = new SizedPopup();
        popup.SetModalOwner(first);
        var nested = new MessageBoxWindow().SetTitleText("Nested question");
        try
        {
            var popupClosed = popup.ShowDialog();
            Assert.Same(first, TopLevel.GetTopLevel(popup));
            Assert.False(first.CanInteract);
            Assert.True(second.CanInteract);
            nested.SetModalOwner(popup);
            var nestedClosed = nested.ShowDialog();
            Assert.Same(first, TopLevel.GetTopLevel(nested));
            Assert.Same(nested, first.Modals.TopModal);
            nested.Close();
            await nestedClosed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(popup, first.Modals.TopModal);
            Assert.False(first.CanInteract);
            popup.Close();
            await popupClosed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(first.CanInteract);
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Popup_ContentDoesNotFollowMainWindowScale(double mainWindowScale)
    {
        var resources = Application.Current!.Resources;
        var hadScale = resources.TryGetValue(WindowAppearance.ScaleResourceKey, out var previousScale);
        resources[WindowAppearance.ScaleResourceKey] = 1.0;
        var content = new SizedPopup();
        var popup = new TestWindow { Width = 1280, Height = 720 };
        popup.Show();
        try
        {
            content.Show();
            popup.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var size = popup.ClientSize;
            var textBounds = content.Label.Bounds;
            var buttonBounds = content.BottomButton.Bounds;

            resources[WindowAppearance.ScaleResourceKey] = mainWindowScale;
            popup.InvalidateMeasure();
            popup.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(size, popup.ClientSize);
            Assert.Equal(textBounds, content.Label.Bounds);
            Assert.Equal(buttonBounds, content.BottomButton.Bounds);
            Assert.Equal(16, content.Label.FontSize);
            AssertInsideWindow(content.BottomButton, popup);
        }
        finally
        {
            popup.Close();
            if (hadScale)
                resources[WindowAppearance.ScaleResourceKey] = previousScale;
            else
                resources.Remove(WindowAppearance.ScaleResourceKey);
        }
    }

    [AvaloniaFact]
    public void DisabledWindow_RejectsNativeCloseButAllowsCompletionAndShutdown()
    {
        var window = new TestWindow();
        try
        {
            window.SetInteractable(false);
            var native = CreateClosingArgs(false);
            window.RequestClose(native);
            Assert.True(native.Cancel);
            foreach (
                var args in new[]
                {
                    CreateClosingArgs(true),
                    CreateClosingArgs(false, WindowCloseReason.OwnerWindowClosing),
                    CreateClosingArgs(false, WindowCloseReason.ApplicationShutdown),
                }
            )
            {
                window.RequestClose(args);
                Assert.False(args.Cancel);
            }
            window.SetInteractable(true);
            var allowed = CreateClosingArgs(false);
            window.RequestClose(allowed);
            Assert.False(allowed.Cancel);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Modal_FollowsContentGrowthAndShrinkage_WithinTheLandscapeViewport()
    {
        var content = new SizedPopup();
        var popup = new TestWindow { Width = 1280, Height = 720 };
        popup.Show();
        try
        {
            content.Show();
            popup.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var initialSize = content.Bounds.Size;
            foreach (var height in new[] { 330.0, 200.0, 900.0, 260.0, 200.0 })
            {
                content.Root.Height = height;
                popup.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(initialSize.Width, content.Bounds.Width);
                Assert.True(content.Bounds.Height <= popup.ClientSize.Height - 48);
                Assert.True(content.Bounds.Height <= height);
                Assert.Equal(16, content.Label.FontSize);
                AssertInsideWindow(content.BottomButton, popup);
            }
        }
        finally
        {
            popup.Close();
        }
    }

    [AvaloniaFact]
    public void MessagePopup_GrowsForLateContentWithoutEnlargingItsTitle()
    {
        var content = new MessageBoxWindow().SetTitleText("A stable title").SetInfoText("Loading...");
        using var owner = new TestWindow { Width = 656, Height = 876 };
        owner.Show();
        content.Show();
        var popup = Assert.IsType<TestWindow>(TopLevel.GetTopLevel(content));
        popup.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var initialSize = content.Bounds.Size;
        var title = content.FindControl<SelectableTextBlock>("MessageTitleBlock")!;
        var titleSize = title.Bounds.Size;
        var titleFontSize = title.FontSize;

        content.SetInfoText(string.Join("\n", Enumerable.Repeat("Information loaded after opening.", 15)));
        popup.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.True(content.Bounds.Height > initialSize.Height);
        Assert.Equal(initialSize.Width, content.Bounds.Width);
        Assert.Equal(titleSize, title.Bounds.Size);
        Assert.Equal(titleFontSize, title.FontSize);
        foreach (var button in content.GetVisualDescendants().OfType<Button>())
            AssertInsideWindow(button, popup);
    }

    private static void AssertInsideWindow(Control control, Window popup)
    {
        var position = control.TranslatePoint(default, popup)!.Value;
        Assert.True(position.X >= 0 && position.Y >= 30);
        Assert.True(position.X + control.Bounds.Width <= popup.ClientSize.Width);
        Assert.True(position.Y + control.Bounds.Height <= popup.ClientSize.Height);
    }

    [AvaloniaFact]
    public async Task PlayerProfilePopup_FitsLateProfileAndHistoryWithoutScalingContent()
    {
        var profileReady = new TaskCompletionSource<OperationResult<PlayerProfileResponse>>();
        var historyReady = new TaskCompletionSource<OperationResult<RwfcPlayerVrHistoryResponse>>();
        var caller = Substitute.For<IApiCaller<IRwfcApi>>();
        caller.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>()).Returns(profileReady.Task);
        caller.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>()).Returns(historyReady.Task);
        using var model = new VrHistoryViewModel(caller);
        var history = new VrHistoryGraph(model);
        var content = new PlayerProfileWindow(history, caller, "1234-5678-9012");
        var loaded = new TaskCompletionSource();
        content.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(content.IsLoading) && !content.IsLoading)
                loaded.TrySetResult();
        };
        using var owner = new TestWindow { Width = 656, Height = 876 };
        owner.Show();
        content.Show();
        var popup = Assert.IsType<TestWindow>(TopLevel.GetTopLevel(content));
        popup.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var initialWidth = popup.ClientSize.Width;
        Assert.True(content.IsLoading);

        profileReady.SetResult(
            new PlayerProfileResponse
            {
                Name = "Profile layout test",
                FriendCode = "1234-5678-9012",
                LastSeen = DateTime.UtcNow,
                Vr = 12000,
                Rank = 42,
            }
        );
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(3));
        popup.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var title = content.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Profile layout test");
        var titleSize = title.Bounds.Size;
        Assert.Equal(20, title.FontSize);
        Assert.Equal(initialWidth, popup.ClientSize.Width);
        AssertInsideWindow(title, popup);

        historyReady.SetResult(
            new RwfcPlayerVrHistoryResponse
            {
                FromDate = DateTime.UtcNow.AddDays(-1),
                ToDate = DateTime.UtcNow,
                StartingVr = 11000,
                EndingVr = 12000,
                TotalVrChange = 1000,
                History =
                [
                    new() { Date = DateTime.UtcNow.AddDays(-1), TotalVr = 11000 },
                    new()
                    {
                        Date = DateTime.UtcNow,
                        TotalVr = 12000,
                        VrChange = 1000,
                    },
                ],
            }
        );
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        popup.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(history.HasData);
        Assert.False(history.IsLoading);
        Assert.Equal(initialWidth, popup.ClientSize.Width);
        Assert.Equal(titleSize, title.Bounds.Size);
        Assert.Equal(20, title.FontSize);
        AssertInsideWindow(title, popup);
        AssertInsideWindow(content.GetVisualDescendants().OfType<ScrollViewer>().First(), popup);
        Assert.Same(content, owner.Modals.TopModal);
        Assert.False(owner.CanInteract);
    }

    private sealed class SizedPopup : ModalContent
    {
        public Grid Root { get; }
        public TextBlock Label { get; } = new() { Text = "Unscaled modal content", FontSize = 16 };
        public Button BottomButton { get; } = new() { Content = "Bottom button" };

        public SizedPopup()
            : base("Scale test")
        {
            Root = new Grid
            {
                Width = 300,
                Height = 200,
                RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            };
            Grid.SetRow(BottomButton, 2);
            Root.Children.Add(Label);
            Root.Children.Add(BottomButton);
            Content = Root;
        }
    }

    private static WindowClosingEventArgs CreateClosingArgs(
        bool programmatic,
        WindowCloseReason reason = WindowCloseReason.WindowClosing
    ) =>
        (WindowClosingEventArgs)
            Activator.CreateInstance(
                typeof(WindowClosingEventArgs),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                args: [reason, programmatic],
                culture: null
            )!;

    private sealed class TestWindow : BaseWindow, IDisposable
    {
        public void Dispose() => Close();
        public void RequestClose(WindowClosingEventArgs args) => OnClosing(args);

        private readonly Border _overlay = new();
        private readonly Border _content = new();
        protected override Control InteractionOverlay => _overlay;
        protected override Control InteractionContent => _content;
        public bool CanInteract => _content.IsEnabled;

        public TestWindow()
        {
            Content = _content;
        }
    }
}
