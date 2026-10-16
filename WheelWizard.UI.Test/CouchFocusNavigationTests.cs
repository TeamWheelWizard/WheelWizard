using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using WheelWizard.Views.Shell.Couch.Input;

namespace WheelWizard.UI.Test;

public class CouchFocusNavigationTests
{
    [AvaloniaFact]
    public void DirectionalFocus_ReachesAndRevealsAControlBelowTheScrollViewport()
    {
        var first = new Button { Content = "First", Height = 48 };
        var last = new Button { Content = "Last", Height = 48 };
        var scroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    first,
                    new Border { Height = 300 },
                    last,
                },
            },
        };
        var window = new Window
        {
            Width = 400,
            Height = 180,
            Content = scroll,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            first.Focus(NavigationMethod.Directional);
            Assert.Equal(0, scroll.Offset.Y);
            Assert.True(last.TranslatePoint(default, scroll)!.Value.Y > scroll.Viewport.Height);
            Assert.True(CouchInputController.MoveFocus(window, first, NavigationDirection.Down));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Same(last, window.FocusManager!.GetFocusedElement());
            Assert.True(scroll.Offset.Y > 0);
            var point = last.TranslatePoint(default, scroll)!.Value;
            Assert.InRange(point.Y, 0, scroll.Viewport.Height - last.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DirectionalFocus_DoesNotEscapeTheScopeForACloserUnderlyingAction()
    {
        var first = new Button { Content = "Modal first", Height = 48 };
        var last = new Button { Content = "Modal last", Height = 48 };
        var scope = new StackPanel
        {
            Width = 200,
            Spacing = 170,
            Children = { first, last },
        };
        var outside = new Button
        {
            Content = "Underlying action",
            Height = 48,
            Width = 200,
            Margin = new Thickness(0, 70, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window
        {
            Width = 400,
            Height = 320,
            Content = new Grid { Children = { scope, outside } },
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            first.Focus(NavigationMethod.Directional);
            Assert.True(CouchInputController.MoveFocus(scope, first, NavigationDirection.Down));
            Assert.Same(last, window.FocusManager!.GetFocusedElement());
            Assert.NotSame(outside, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }
}
