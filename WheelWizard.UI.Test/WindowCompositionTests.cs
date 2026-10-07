using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using WheelWizard.Settings.Types;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Controls;

namespace WheelWizard.UI.Test;

public class WindowCompositionTests
{
    [AvaloniaTheory]
    [InlineData(64.0)]
    [InlineData(221.0)]
    public void SidebarHover_MovesGlowWithoutChangingLayout(double width)
    {
        var button = new WheelWizard.Views.Shell.Controls.SidebarRadioButton { Text = "Home" };
        var window = new Window
        {
            Width = width,
            Height = 46,
            Content = button,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var bounds = button.Bounds;
            var desiredSize = button.DesiredSize;
            var glow = button.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_HoverEffect");
            Assert.False(glow.IsHitTestVisible);
            foreach (var x in new[] { 2.0, width / 2, width - 1 })
            {
                window.MouseMove(new Point(x, 23));
                window.UpdateLayout();
                Assert.True(glow.IsVisible);
                Assert.Equal(x - 23, Assert.IsType<TranslateTransform>(glow.RenderTransform).X);
                Assert.Equal(bounds, button.Bounds);
                Assert.Equal(desiredSize, button.DesiredSize);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(400.0)]
    [InlineData(500.0)]
    public void ProgressContent_KeepsDefinedMeasuredWidth_ButFillsExtraAvailableWidth(double preferredWidth)
    {
        var content = new WheelWizard.Views.Dialogs.ProgressWindow("Download");
        Assert.IsType<Grid>(content.Content).Width = preferredWidth;
        content.SetGoal(new string('W', 150));
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(preferredWidth, content.DesiredSize.Width);
        content.Arrange(new Rect(0, 0, 600, 200));
        Assert.Equal(600, Assert.IsType<Grid>(content.Content).Bounds.Width);
        Assert.Equal(600, content.FindControl<ProgressBar>("ProgressBar")!.Bounds.Width);
        content.Close();
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
    public void DesktopCaptionButtons_UseNavigationColorsAndMinimizeHoverBackground()
    {
        var popup = new PopupWindow(true, false, false, "Caption test");
        try
        {
            popup.Show();
            var host = popup.GetVisualParent()!;
            // Headless windows do not request drawn chrome; request it through the same host path.
            var update = host.GetType()
                .GetMethod("UpdateDrawnDecorations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var parts = update.GetParameters()[0].ParameterType.GetGenericArguments()[0];
            update.Invoke(
                host,
                [Enum.Parse(parts, "TitleBar, Border"), WindowState.Normal, Application.Current!.FindResource("DesktopWindowDecorations")]
            );
            popup.UpdateLayout();
            var buttons = host.GetVisualDescendants().OfType<Button>().ToArray();
            var minimize = buttons.Single(button => button.Name == "PART_MinimizeButton");
            var maximize = buttons.Single(button => button.Name == "PART_MaximizeButton");
            var close = buttons.Single(button => button.Name == "PART_CloseButton");
            Assert.Equal(
                Application.Current!.FindResource("Neutral400"),
                Assert.IsAssignableFrom<ISolidColorBrush>(minimize.Foreground).Color
            );
            Assert.Equal(
                Application.Current!.FindResource("Neutral400"),
                Assert.IsAssignableFrom<ISolidColorBrush>(close.Foreground).Color
            );
            Assert.False(maximize.IsEnabled);
            Assert.Equal(
                Application.Current!.FindResource("Neutral950"),
                Assert.IsAssignableFrom<ISolidColorBrush>(maximize.Foreground).Color
            );
            Assert.Equal(1, maximize.Opacity);
            ((IPseudoClasses)minimize.Classes).Set(":pointerover", true);
            var background = minimize.GetVisualDescendants().OfType<ContentPresenter>().Single();
            Assert.Equal(
                Application.Current!.FindResource("Neutral600"),
                Assert.IsAssignableFrom<ISolidColorBrush>(background.Background).Color
            );
        }
        finally
        {
            popup.Close();
        }
    }

    [AvaloniaFact]
    public void PopupScale_UsesApplicationResourcesWithoutAServiceProvider()
    {
        Application.Current!.Resources[WindowAppearance.ScaleResourceKey] = 1.5;
        var popup = new PopupWindow(true, false, false, "Scale test");
        try
        {
            popup.Show();
            popup.UpdateLayout();
            Assert.Equal(1.5, popup.RequestedWindowScale);
            Application.Current.Resources[WindowAppearance.ScaleResourceKey] = 1.25;
            Assert.Equal(1.25, popup.RequestedWindowScale);
        }
        finally
        {
            popup.Close();
            Application.Current.Resources.Remove(WindowAppearance.ScaleResourceKey);
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

    [AvaloniaFact]
    public void ClosingOwner_ClosesItsNonmodalPopups()
    {
        var owner = new TestWindow();
        var popup = new TestWindow(allowParentInteraction: true);
        owner.Show();
        popup.Show(owner);
        Assert.True(owner.CanInteract);
        owner.Close();
        Assert.False(popup.IsVisible);
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
    [InlineData(0.75)]
    [InlineData(1.5)]
    public void Popup_TitleBarDoesNotScaleWithContent(double scale)
    {
        var popup = new PopupWindow(true, false, false, "Scale test") { RequestedWindowScale = scale };
        try
        {
            popup.SetWindowSize(new Size(300, 200));
            Assert.Equal(300 * scale, popup.Width);
            Assert.Equal(200 * scale + 30, popup.Height);
            Assert.Equal(30, popup.ExtendClientAreaTitleBarHeightHint);
            Assert.Equal(WindowDecorations.Full, popup.WindowDecorations);
            var root = Assert.IsType<Grid>(popup.Content);
            Assert.Null(root.RenderTransform);
            Assert.Equal(30, root.RowDefinitions[0].Height.Value);
        }
        finally
        {
            popup.Close();
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopupSize_ScreenCapCanReduceScaleBelowConfiguredMinimum(bool constrainedWidth)
    {
        var popup = new PopupWindow { RequestedWindowScale = SettingValues.MinWindowScale };
        try
        {
            var screen = popup.Screens.Primary;
            Assert.NotNull(screen);
            var available = new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
            var size = constrainedWidth
                ? new Size(available.Width * 1.25 / SettingValues.MinWindowScale, 200)
                : new Size(300, available.Height / SettingValues.MinWindowScale);
            popup.SetWindowSize(size);
            Assert.InRange(popup.Width, double.Epsilon, available.Width + 0.001);
            Assert.InRange(popup.Height, 30, available.Height + 0.001);
            Assert.True(popup.Width / size.Width < SettingValues.MinWindowScale);
        }
        finally
        {
            popup.Close();
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
