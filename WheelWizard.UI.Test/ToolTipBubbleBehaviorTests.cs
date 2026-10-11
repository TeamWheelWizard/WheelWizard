using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WheelWizard.UI.Test;

public class ToolTipBubbleBehaviorTests
{
    private static void SetActive(Window window, bool active) =>
        typeof(WindowBase)
            .GetMethod(
                active ? "HandleActivated" : "HandleDeactivated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .Invoke(window, null);

    [AvaloniaFact]
    public async Task Hover_OpensWithoutTimer_AndExitStartsExistingAnimationImmediately()
    {
        var target = new Border
        {
            Width = 100,
            Height = 100,
            Background = Avalonia.Media.Brushes.Transparent,
        };
        ToolTip.SetTip(target, new WheelWizard.Views.Components.HintTooltip { Text = "Hover" });
        ToolTip.SetPlacement(target, PlacementMode.BottomEdgeAlignedRight);
        var window = new Window
        {
            Content = target,
            Width = 300,
            Height = 300,
        };
        try
        {
            window.Show();
            // Headless windows do not receive native activation notifications.
            SetActive(window, true);
            window.UpdateLayout();
            window.MouseMove(target.TranslatePoint(new Point(50, 50), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(target.IsPointerOver);
            Assert.True(window.IsActive, "Window should be active");
            Assert.False(ToolTip.GetServiceEnabled(target), "Custom tooltip service should be initialized");
            Assert.True(ToolTip.GetIsOpen(target));
            Assert.Equal(PlacementMode.TopEdgeAlignedRight, ToolTip.GetPlacement(target));
            var tip = Assert.IsType<WheelWizard.Views.Components.HintTooltip>(ToolTip.GetTip(target));
            Assert.Contains("BubbleAnimateIn", tip.Classes);
            Assert.Contains("BubblePointerRight", tip.Classes);
            Assert.True(
                tip.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(path => path.Name == "PART_ToolTipTail").IsVisible
            );

            window.MouseMove(new Point(1, 1));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("BubbleAnimateOut", tip.Classes);
            await Task.Delay(80);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ToolTip.GetIsOpen(target));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LosingFocus_ClosesTooltip_AndPreventsHoverReopeningInBackground()
    {
        var target = new Border
        {
            Width = 100,
            Height = 100,
            Background = Avalonia.Media.Brushes.Transparent,
        };
        ToolTip.SetTip(target, "Hover");
        var window = new Window
        {
            Content = target,
            Width = 300,
            Height = 300,
        };
        try
        {
            window.Show();
            // Headless windows do not receive native activation notifications.
            SetActive(window, true);
            window.UpdateLayout();
            window.MouseMove(target.TranslatePoint(new Point(50, 50), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsActive, "Window should be active");
            Assert.False(ToolTip.GetServiceEnabled(target), "Custom tooltip service should be initialized");
            Assert.True(ToolTip.GetIsOpen(target));

            SetActive(window, false);
            Assert.False(ToolTip.GetIsOpen(target));
            window.MouseMove(new Point(1, 1));
            window.MouseMove(target.TranslatePoint(new Point(50, 50), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ToolTip.GetIsOpen(target));
        }
        finally
        {
            window.Close();
        }
    }
}
