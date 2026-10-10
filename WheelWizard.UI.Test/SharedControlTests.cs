using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.Shared;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell.Controls;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.UI.Test;

public class SharedControlTests
{
    [AvaloniaFact]
    public void Button_RendersNativeContentAndOpensItsFlyoutFromKeyboard()
    {
        var content = new TextBlock { Text = "Custom content" };
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuItem { Header = "Import" });
        var button = new Button
        {
            Content = content,
            Flyout = menu,
            Variant = ButtonVariant.Primary,
        };
        button.Classes.Add("caller-class");
        var window = new Window { Content = button };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Contains(content, button.GetVisualDescendants());
            var initialBackground = button.Background;
            button.Variant = ButtonVariant.Danger;
            Assert.NotEqual(initialBackground, button.Background);
            Assert.Equal(new[] { "caller-class" }, button.Classes);
            button.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(menu.IsOpen);
            menu.Hide();
            Assert.False(menu.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SidebarButton_NavigatesFromKeyboardActivation()
    {
        var button = new SidebarRadioButton { PageType = typeof(TextInputWindow) };
        Type? requested = null;
        button.NavigationRequested += (_, page) => requested = page;
        var window = new Window { Content = button };
        try
        {
            window.Show();
            window.UpdateLayout();
            button.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Null(requested);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(typeof(TextInputWindow), requested);
            Assert.True(button.IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TextField_PreservesCallerBindingAndDerivesErrorsFromText()
    {
        var model = new InputModel();
        var field = new TextField { DataContext = model, ErrorText = "Invalid value" };
        field.Bind(TextField.TextProperty, new Binding(nameof(InputModel.Value)));
        var window = new Window
        {
            Content = field,
            Width = 320,
            Height = 160,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var input = Assert.Single(field.GetVisualDescendants().OfType<TextBox>());
            Assert.Same(model, field.DataContext);
            Assert.Equal("Initial", input.Text);
            Assert.True(field.HasError);
            Assert.Contains("error", input.Classes);
            input.SetCurrentValue(TextBox.TextProperty, "Edited");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Edited", model.Value);
            model.Value = "Updated";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Updated", input.Text);
            foreach (var cleared in new string?[] { null, "", "   " })
            {
                field.ErrorText = cleared;
                Assert.False(field.HasError);
                Assert.DoesNotContain("error", input.Classes);
            }
            field.ErrorText = "Duplicate friend code";
            Assert.True(field.HasError);
            Assert.Contains("error", input.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TextInput_ValidationImmediatelyDisablesInvalidInitialValues()
    {
        var dialog = new TextInputWindow()
            .SetInitialText("duplicate")
            .SetValidation((_, text) => text == "duplicate" ? OperationError.Fail("Already in your list") : OperationResult.Ok());
        var field = dialog.FindControl<TextField>("InputField")!;
        var submit = dialog.FindControl<Button>("SubmitButton")!;
        Assert.True(field.HasError);
        Assert.Equal("Already in your list", field.ErrorText);
        Assert.False(submit.IsEnabled);
        dialog.SetInitialText("unique");
        Assert.False(field.HasError);
        Assert.True(submit.IsEnabled);
    }

    [AvaloniaFact]
    public void LinkButton_UsesNativeKeyboardClickAndDisabledBehavior()
    {
        var button = new LinkButton
        {
            Text = "Open",
            Width = 100,
            Height = 40,
        };
        var window = new Window { Content = button };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        try
        {
            window.Show();
            window.UpdateLayout();
            button.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(0, clicks);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(1, clicks);
            button.IsEnabled = false;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(1, clicks);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HoverGlow_MovesWithoutChangingButtonLayout()
    {
        foreach (Control button in new Control[] { new TileButton(), new ListActionButton(), new SidebarRadioButton() })
        {
            var window = new Window
            {
                Content = button,
                Width = 300,
                Height = 200,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var before = button.Bounds;
                var desired = button.DesiredSize;
                var glow = Assert.Single(button.GetVisualDescendants().OfType<HoverGlow>());
                var transform = Assert.IsType<TranslateTransform>(glow.RenderTransform);
                window.MouseMove(button.TranslatePoint(new Point(10, 10), window)!.Value);
                var initialX = transform.X;
                window.MouseMove(button.TranslatePoint(new Point(button.Bounds.Width - 2, 15), window)!.Value);
                window.UpdateLayout();
                Assert.NotEqual(initialX, transform.X);
                Assert.Equal(before, button.Bounds);
                Assert.Equal(desired, button.DesiredSize);
                Assert.False(glow.IsHitTestVisible);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void AspectGrid_ResizesWithoutPinningWidthOrHeight()
    {
        var grid = new AspectGrid { AspectRatio = 2 };
        grid.Children.Add(new Border());
        foreach (var available in new[] { new Size(300, 100), new Size(120, 200), new Size(500, 300) })
        {
            grid.Measure(available);
            grid.Arrange(new Rect(available));
            Assert.Equal(2, grid.Bounds.Width / grid.Bounds.Height);
            Assert.True(grid.Bounds.Width <= available.Width);
            Assert.True(grid.Bounds.Height <= available.Height);
            Assert.True(double.IsNaN(grid.Width));
            Assert.True(double.IsNaN(grid.Height));
        }
        Assert.Throws<ArgumentException>(() => grid.AspectRatio = 0);
    }

    private sealed class InputModel : INotifyPropertyChanged
    {
        private string? _value = "Initial";
        public string? Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
