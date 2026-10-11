using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WheelWizard.Models;
using WheelWizard.RrRooms;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
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
        var window = new Window
        {
            Content = host,
            Width = 221,
            Height = 46,
        };
        try
        {
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
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsContent_StaysStationaryWhileNavigationIsRevealed()
    {
        var pages = Substitute.For<WheelWizard.Views.Shell.Navigation.IPageFactory>();
        pages.Create(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(_ => new UserControl());
        var page = new WheelWizard.Settings.Views.SettingsPage(
            Substitute.For<IPopupFactory>(),
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
        var window = new Window
        {
            Content = host,
            Width = 572,
            Height = 700,
        };
        try
        {
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
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ProgressNativeClose_CancelsOnlyCancelableUserRequests(bool canCancel, bool programmatic)
    {
        using var cancellation = new CancellationTokenSource();
        var content = new WheelWizard.Views.Dialogs.ProgressWindow("Download");
        if (canCancel)
            content.SetCancellationTokenSource(cancellation);
        content.Show();
        var popup = Assert.IsType<PopupWindow>(TopLevel.GetTopLevel(content));
        try
        {
            var args = CreateClosingArgs(programmatic);
            typeof(PopupWindow)
                .GetMethod("OnClosing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(popup, [args]);
            Assert.Equal(canCancel && !programmatic, cancellation.IsCancellationRequested);
            Assert.Equal(canCancel && !programmatic, content.WasCancellationRequested);
            Assert.Equal(!programmatic, args.Cancel);
        }
        finally
        {
            content.SetCancellationTokenSource(null);
            content.Close();
        }
    }

    [AvaloniaFact]
    public void OwnedPopups_DisableOnlyTheirWindowGroup_AndRestoreInteraction()
    {
        var first = new TestWindow();
        var second = new TestWindow();
        var popup = new TestWindow();
        var nested = new TestWindow();
        try
        {
            first.Show();
            second.Show();
            Assert.True(first.CanInteract);
            Assert.True(second.CanInteract);
            popup.Show(first);
            Assert.False(first.CanInteract);
            Assert.True(second.CanInteract);
            nested.Show(popup);
            Assert.False(popup.CanInteract);
            nested.Close();
            Assert.True(popup.CanInteract);
            Assert.False(first.CanInteract);
            popup.Close();
            Assert.True(first.CanInteract);
        }
        finally
        {
            nested.Close();
            popup.Close();
            first.Close();
            second.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopupShow_PreservesOwnershipAndClosesWithItsOwner(bool allowParentInteraction)
    {
        var owner = new TestWindow();
        owner.Show();
        var popup = new OwnedPopup(owner, allowParentInteraction);
        try
        {
            popup.Show();
            Assert.Same(owner, popup.Owner);
            Assert.Equal(allowParentInteraction, owner.CanInteract);
            owner.Close();
            Assert.False(popup.IsVisible);
        }
        finally
        {
            popup.Close();
            owner.Close();
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
        var popup = content.Host;
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
            Assert.Equal(30, popup.ExtendClientAreaTitleBarHeightHint);
            Assert.Equal(WindowDecorations.Full, popup.WindowDecorations);
            Assert.Equal(SizeToContent.WidthAndHeight, popup.SizeToContent);
            Assert.Null(popup.FindControl<Grid>("CompleteGrid")!.RenderTransform);
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
    public void PopupDimming_CoversTitleBarAndContent_AndRestoresTogether()
    {
        var popup = new PopupWindow(true, false, false, "Dimming test");
        try
        {
            popup.Show();
            popup.UpdateLayout();
            var frame = Assert.IsType<Grid>(popup.Content);
            var overlay = popup.FindControl<Border>("DisabledDarkenEffect")!;
            popup.SetInteractable(false);
            popup.UpdateLayout();
            Assert.Contains(overlay, frame.Children);
            Assert.Equal(2, Grid.GetRowSpan(overlay));
            Assert.True(overlay.IsVisible);
            Assert.Equal(frame.Bounds.Height, overlay.Bounds.Height);
            Assert.False(frame.IsEnabled);
            popup.SetInteractable(true);
            Assert.False(overlay.IsVisible);
            Assert.True(frame.IsEnabled);
        }
        finally
        {
            popup.Close();
        }
    }

    [AvaloniaFact]
    public void NonClosablePopup_RejectsNativeCloseButAllowsProgrammaticCompletion()
    {
        var popup = new ClosingPopup();
        var userClose = CreateClosingArgs(false);
        popup.RequestClose(userClose);
        Assert.True(userClose.Cancel);
        var completion = CreateClosingArgs(true);
        popup.RequestClose(completion);
        Assert.False(completion.Cancel);
        popup.CanClose = true;
        var allowed = CreateClosingArgs(false);
        popup.RequestClose(allowed);
        Assert.False(allowed.Cancel);
        popup.Close();
    }

    [AvaloniaFact]
    public void DisabledWindows_RejectNativeCloseButAllowCompletionAndShutdown()
    {
        var main = new TestWindow();
        var popup = new ClosingPopup { CanClose = true };
        (BaseWindow Window, Action<WindowClosingEventArgs> RequestClose)[] windows =
        [
            (main, main.RequestClose),
            (popup, popup.RequestClose),
        ];
        foreach (var (window, requestClose) in windows)
        {
            try
            {
                window.SetInteractable(false);
                var native = CreateClosingArgs(false);
                requestClose(native);
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
                    requestClose(args);
                    Assert.False(args.Cancel);
                }
                window.SetInteractable(true);
                var allowed = CreateClosingArgs(false);
                requestClose(allowed);
                Assert.False(allowed.Cancel);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Popup_AutoSizingFollowsContentGrowthAndShrinkage()
    {
        var content = new SizedPopup();
        var popup = content.Host;
        try
        {
            content.Show();
            popup.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var initialSize = popup.ClientSize;
            foreach (var height in new[] { 330.0, 200.0, 260.0, 200.0 })
            {
                content.Root.Height = height;
                popup.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(initialSize.Width, popup.ClientSize.Width);
                Assert.Equal(initialSize.Height + height - 200, popup.ClientSize.Height);
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
        try
        {
            content.Show();
            var popup = Assert.IsType<PopupWindow>(TopLevel.GetTopLevel(content));
            popup.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var initialSize = popup.ClientSize;
            var title = content.FindControl<SelectableTextBlock>("MessageTitleBlock")!;
            var titleSize = title.Bounds.Size;
            var titleFontSize = title.FontSize;

            content.SetInfoText(string.Join("\n", Enumerable.Repeat("Information loaded after opening.", 15)));
            popup.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(popup.ClientSize.Height > initialSize.Height);
            Assert.Equal(initialSize.Width, popup.ClientSize.Width);
            Assert.Equal(titleSize, title.Bounds.Size);
            Assert.Equal(titleFontSize, title.FontSize);
            foreach (var button in content.GetVisualDescendants().OfType<Button>())
                AssertInsideWindow(button, popup);
        }
        finally
        {
            content.Close();
        }
    }

    private static void AssertInsideWindow(Control control, PopupWindow popup)
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
        try
        {
            content.Show();
            var popup = Assert.IsType<PopupWindow>(TopLevel.GetTopLevel(content));
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
            Assert.Null(popup.FindControl<Grid>("CompleteGrid")!.RenderTransform);
        }
        finally
        {
            content.Close();
        }
    }

    private sealed class SizedPopup : PopupContent
    {
        public PopupWindow Host => Window;
        public Grid Root { get; }
        public TextBlock Label { get; } = new() { Text = "Unscaled modal content", FontSize = 16 };
        public Button BottomButton { get; } = new() { Content = "Bottom button" };

        public SizedPopup()
            : base(true, false, false, "Scale test")
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

    private sealed class ClosingPopup : PopupWindow
    {
        public void RequestClose(WindowClosingEventArgs args) => OnClosing(args);
    }

    private sealed class OwnedPopup : PopupWindow
    {
        public OwnedPopup(Window owner, bool allowParentInteraction)
            : base(true, allowParentInteraction, false, "Owned popup")
        {
            Owner = owner;
        }
    }

    private sealed class TestWindow : BaseWindow
    {
        public void RequestClose(WindowClosingEventArgs args) => OnClosing(args);

        private readonly Border _overlay = new();
        private readonly Border _content = new();
        protected override Control InteractionOverlay => _overlay;
        protected override Control InteractionContent => _content;
        public bool CanInteract => _content.IsEnabled;

        public TestWindow(bool allowParentInteraction = false)
        {
            AllowParentInteraction = allowParentInteraction;
            Content = _content;
        }
    }
}
