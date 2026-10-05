using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.UI.Test;

public class WindowCompositionTests
{
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
            Assert.Equal(Application.Current!.FindResource("Neutral400"), Assert.IsAssignableFrom<ISolidColorBrush>(close.Foreground).Color);
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

    private static WindowClosingEventArgs CreateClosingArgs(bool programmatic) =>
        (WindowClosingEventArgs)
            Activator.CreateInstance(
                typeof(WindowClosingEventArgs),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                args: [WindowCloseReason.WindowClosing, programmatic],
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
