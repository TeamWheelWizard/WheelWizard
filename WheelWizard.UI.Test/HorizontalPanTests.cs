using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Threading;
using WheelWizard.Views.Behaviors;
using WheelWizard.Views.Components;

namespace WheelWizard.UI.Test;

public class HorizontalPanTests
{
    [AvaloniaFact]
    public void DraggingContentPansWithoutClicking_OrdinaryClicksStillWork()
    {
        var clicks = 0;
        var buttons = Enumerable.Range(0, 8).Select(_ => new ActionButton { Width = 100, Content = "Image" }).ToArray();
        foreach (var button in buttons)
            button.Click += (_, _) => clicks++;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in buttons)
            panel.Children.Add(button);
        var scroll = new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        using var owner = new UiTestHost { Width = 320, Height = 150 };
        owner.Body.Child = scroll;
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(HorizontalPanBehavior.GetIsEnabled(scroll));
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
        var start = buttons[1].TranslatePoint(new Point(50, buttons[1].Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(start, MouseButton.Left);
        owner.MouseMove(start - new Point(90, 0), RawInputModifiers.LeftMouseButton);
        owner.MouseUp(start - new Point(90, 0), MouseButton.Left);
        Assert.InRange(scroll.Offset.X, 89, 91);
        Assert.Equal(0, clicks);
        owner.UpdateLayout();
        var click = buttons[1].TranslatePoint(new Point(50, buttons[1].Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(click, MouseButton.Left);
        owner.MouseUp(click, MouseButton.Left);
        Assert.Equal(1, clicks);
        owner.MouseDown(click, MouseButton.Left);
        owner.MouseMove(click + new Point(100, 0), RawInputModifiers.LeftMouseButton);
        owner.MouseUp(click + new Point(100, 0), MouseButton.Left);
        Assert.Equal(0, scroll.Offset.X);
    }

    [AvaloniaFact]
    public void DraggingInputTextDoesNotPanItsParent()
    {
        var input = new InputField
        {
            Text = "Select this text",
            Width = 800,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var scroll = new ScrollViewer { Content = input, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        using var owner = new UiTestHost { Width = 320, Height = 150 };
        owner.Body.Child = scroll;
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        var start = input.TranslatePoint(new Point(120, input.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(start, MouseButton.Left);
        owner.MouseMove(start - new Point(80, 0), RawInputModifiers.LeftMouseButton);
        owner.MouseUp(start - new Point(80, 0), MouseButton.Left);
        Assert.Equal(0, scroll.Offset.X);
        Assert.NotEqual(input.SelectionStart, input.SelectionEnd);
    }
}
