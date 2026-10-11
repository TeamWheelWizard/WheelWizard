using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
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
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Controls;

namespace WheelWizard.UI.Test;

public class SharedControlTests
{
    [AvaloniaFact]
    public void ButtonLoading_ReplacesItsIconAndPreservesItsLabel()
    {
        var button = new ActionButton { Text = "Load", IconData = Geometry.Parse("M0 0 H16 V16 H0 Z") };
        var window = new Window
        {
            Content = button,
            Width = 240,
            Height = 80,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            button.IsLoading = true;
            Assert.Equal("Load", button.Content);
            Assert.All(button.GetVisualDescendants().OfType<PathIcon>(), icon => Assert.False(icon.IsVisible));
            var spinner = Assert.Single(
                button.GetVisualDescendants().OfType<WheelWizard.Views.Components.Spinner>(),
                control => control.IsVisible
            );
            Assert.Equal("PART_LeftSpinner", spinner.Name);
            button.IsIconLeft = false;
            Assert.Equal(
                "PART_RightSpinner",
                Assert
                    .Single(button.GetVisualDescendants().OfType<WheelWizard.Views.Components.Spinner>(), control => control.IsVisible)
                    .Name
            );
            window.Resources["ControlAnimationsEnabled"] = false;
            Assert.False(spinner.IsSpinning);
            button.IsLoading = false;
            Assert.Single(button.GetVisualDescendants().OfType<PathIcon>(), icon => icon.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SplitButton_SeparatesTheMainActionFromItsMenuAndRespectsDisabledItems()
    {
        var menu = new MenuFlyout();
        var item = new SplitButtonItem { Header = "Import", IconData = Geometry.Parse("M0 0 H16 V16 H0 Z") };
        var disabled = new SplitButtonItem { Header = "Unavailable", IsEnabled = false };
        menu.Items.Add(item);
        menu.Items.Add(disabled);
        var split = new WheelWizard.Views.Components.SplitButton
        {
            Text = "Browse",
            Tone = ActionButtonTone.Brand,
            Flyout = menu,
        };
        var mainClicks = 0;
        var menuClicks = 0;
        split.Click += (_, _) => mainClicks++;
        item.Click += (_, _) => menuClicks++;
        var window = new Window
        {
            Content = split,
            Width = 300,
            Height = 100,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var primary = split.GetVisualDescendants().OfType<ActionButton>().Single(button => button.Name == "PART_PrimaryButton");
            var secondary = split.GetVisualDescendants().OfType<ActionButton>().Single(button => button.Name == "PART_SecondaryButton");
            primary.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, "");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, "");
            Assert.Equal(1, mainClicks);
            Assert.False(menu.IsOpen);
            var point = secondary.TranslatePoint(new Point(secondary.Bounds.Width / 2, secondary.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(menu.IsOpen);
            Assert.NotNull(menu.FlyoutPresenterTheme);
            Assert.Equal(1, mainClicks);
            var popup = Assert.IsAssignableFrom<WindowBase>(TopLevel.GetTopLevel(item));
            popup.UpdateLayout();
            var itemPoint = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), popup)!.Value;
            popup.MouseMove(itemPoint);
            popup.MouseDown(itemPoint, MouseButton.Left);
            popup.MouseUp(itemPoint, MouseButton.Left);
            Assert.Equal(1, menuClicks);
            Assert.Equal(1, mainClicks);
            Assert.False(menu.IsOpen);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(menu.IsOpen);
            popup = Assert.IsAssignableFrom<WindowBase>(TopLevel.GetTopLevel(disabled));
            popup.UpdateLayout();
            var disabledPoint = disabled.TranslatePoint(new Point(disabled.Bounds.Width / 2, disabled.Bounds.Height / 2), popup)!.Value;
            popup.MouseMove(disabledPoint);
            popup.MouseDown(disabledPoint, MouseButton.Left);
            popup.MouseUp(disabledPoint, MouseButton.Left);
            Assert.Equal(1, menuClicks);
            Assert.True(menu.IsOpen);
            Assert.False(disabled.IsEffectivelyEnabled);
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnimationSettingToggle_UsesTheNewModeBeforeChangingItsSelection()
    {
        var toggle = new WheelWizard.Settings.Views.AnimationSettingToggle
        {
            Variant = ToggleVariant.Switch,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Window
        {
            Content = toggle,
            Width = 300,
            Height = 100,
        };
        window.Resources[WindowAppearance.ControlAnimationDurationResourceKey] = TimeSpan.Zero;
        try
        {
            window.Show();
            window.UpdateLayout();
            window.MouseDown(new Point(10, 10), MouseButton.Left);
            window.MouseUp(new Point(10, 10), MouseButton.Left);
            Assert.True(toggle.IsChecked);
            Assert.Equal(TimeSpan.FromMilliseconds(120), toggle.Resources[WindowAppearance.ControlAnimationDurationResourceKey]);
            window.MouseDown(new Point(10, 10), MouseButton.Left);
            window.MouseUp(new Point(10, 10), MouseButton.Left);
            Assert.False(toggle.IsChecked);
            Assert.Equal(TimeSpan.Zero, toggle.Resources[WindowAppearance.ControlAnimationDurationResourceKey]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SegmentedControl_UsesEqualWidthsAndMovesItsSingleSelection()
    {
        var segments = new SegmentedControl { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var first = new SegmentOption { Text = "Short" };
        var second = new SegmentOption { Text = "A considerably longer option", IconData = Geometry.Parse("M0 0 H16 V16 H0 Z") };
        var third = new SegmentOption { IconData = first.IconData = second.IconData, IsEnabled = false };
        segments.Items.Add(first);
        segments.Items.Add(second);
        segments.Items.Add(third);
        var window = new Window
        {
            Content = segments,
            Width = 800,
            Height = 80,
        };
        window.Resources["SegmentAnimationDuration"] = TimeSpan.Zero;
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(0, segments.SelectedIndex);
            Assert.Equal(first.Bounds.Width, second.Bounds.Width);
            Assert.Equal(second.Bounds.Width, third.Bounds.Width);
            Assert.True(first.Bounds.Width > 100);
            var indicator = segments.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "PART_SelectionIndicator");
            Assert.True(indicator.IsVisible);
            var initial = ((TranslateTransform)indicator.RenderTransform!).X;
            first.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
            window.UpdateLayout();
            Assert.Equal(1, segments.SelectedIndex);
            Assert.True(((TranslateTransform)indicator.RenderTransform!).X > initial);
            Assert.False(first.IsSelected);
            Assert.True(second.IsSelected);
            Assert.False(third.IsSelected);
            segments.HasBorder = false;
            Assert.Equal(new Thickness(0), segments.BorderThickness);
            segments.Variant = SegmentedControlVariant.Light;
            Assert.Equal((Color)window.FindResource("Neutral900")!, Assert.IsAssignableFrom<ISolidColorBrush>(segments.Background).Color);
            segments.Orientation = Avalonia.Layout.Orientation.Vertical;
            window.Height = 240;
            window.UpdateLayout();
            Assert.Equal((Color)window.FindResource("Neutral950")!, Assert.IsAssignableFrom<ISolidColorBrush>(indicator.Background).Color);
            Assert.Equal(PlacementMode.Right, ToolTip.GetPlacement(first));
            Assert.Equal(first.Bounds.Height, second.Bounds.Height);
            Assert.Equal(second.Bounds.Height, third.Bounds.Height);
            second.Focus();
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
            window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
            window.UpdateLayout();
            Assert.Equal(0, segments.SelectedIndex);
            Assert.Equal(0, ((TranslateTransform)indicator.RenderTransform!).Y);
            first.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            window.UpdateLayout();
            Assert.Equal(1, segments.SelectedIndex);
            Assert.True(((TranslateTransform)indicator.RenderTransform!).Y > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InputField_OutsideClickClearsFocusWithoutConsumingTheClick()
    {
        var field = new InputField
        {
            Text = "Example",
            Width = 140,
            Height = 40,
        };
        var background = new Border { Background = Brushes.Transparent, Child = field };
        var window = new Window
        {
            Content = background,
            Width = 300,
            Height = 200,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            field.Focus();
            Assert.True(field.IsFocused);
            window.MouseDown(new Point(10, 10), MouseButton.Left);
            window.MouseUp(new Point(10, 10), MouseButton.Left);
            Assert.False(field.IsFocused);
            Assert.Equal("Example", field.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActionButton_ActivatesOnReleaseAndPreservesNativeContentAndFlyout()
    {
        var button = new ActionButton
        {
            Text = "Run",
            Width = 160,
            Height = 40,
            Flyout = new Flyout { Content = new TextBlock { Text = "Options" } },
        };
        var window = new Window
        {
            Content = button,
            Width = 300,
            Height = 100,
        };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal("Run", button.Content);
            button.Focus();
            var label = button.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "PART_Label");
            var labelPosition = label.TranslatePoint(default, button);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.UpdateLayout();
            Assert.Equal(labelPosition, label.TranslatePoint(default, button));
            Assert.Equal(0, clicks);
            Assert.True(button.IsPressed);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(1, clicks);
            Assert.True(button.Flyout.IsOpen);
            button.Flyout.Hide();
            var content = new TextBlock { Text = "Custom label" };
            button.Content = content;
            window.UpdateLayout();
            Assert.Contains(content, button.GetVisualDescendants());
            button.IsEnabled = false;
            button.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(1, clicks);
            Assert.False(button.Flyout.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dropdown_DismissalBlocksPageClicksButAllowsOptedInNavigation()
    {
        var dropdown = new Dropdown { ItemsSource = new[] { "One", "Two" }, MenuAnimationDuration = TimeSpan.Zero };
        var contentButton = new Avalonia.Controls.Button { Content = "Page action" };
        var navigationButton = new Avalonia.Controls.Button { Content = "Navigation" };
        var navigation = new Border { Child = navigationButton };
        Dropdown.SetAllowDismissInteraction(navigation, true);
        var window = new Window
        {
            Content = new StackPanel
            {
                Children =
                {
                    dropdown,
                    new Border { Height = 140 },
                    contentButton,
                    navigation,
                },
            },
            Width = 320,
            Height = 300,
        };
        var pageClicks = 0;
        var navigationClicks = 0;
        contentButton.Click += (_, _) => pageClicks++;
        navigationButton.Click += (_, _) => navigationClicks++;
        try
        {
            window.Show();
            window.UpdateLayout();
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            var pagePoint = contentButton.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseDown(pagePoint, MouseButton.Left);
            window.MouseUp(pagePoint, MouseButton.Left);
            Assert.False(dropdown.IsDropDownOpen);
            Assert.Equal(0, pageClicks);
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            var navigationPoint = navigationButton.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseDown(navigationPoint, MouseButton.Left);
            window.MouseUp(navigationPoint, MouseButton.Left);
            Assert.False(dropdown.IsDropDownOpen);
            Assert.Equal(1, navigationClicks);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dropdown_SearchFiltersWithoutChangingSelectionAndShowsEmptyStates()
    {
        var options = new[]
        {
            new DropdownOption("Dutch (Nederlands) � a much longer option than the field can fit"),
            new DropdownOption("English"),
            new DropdownOption("Disabled", isEnabled: false),
        };
        var dropdown = new Dropdown
        {
            ItemsSource = options,
            Search = true,
            Width = 140,
            SelectedIndex = 1,
            MenuAnimationDuration = TimeSpan.Zero,
        };
        var window = new Window
        {
            Content = dropdown,
            Width = 320,
            Height = 300,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            dropdown.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var popup = dropdown.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            var search = popup.Child!.GetVisualDescendants().OfType<InputField>().Single();
            var empty = popup.Child.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "PART_Empty");
            var width = popup.Child.Bounds.Width;
            var height = popup.Child.Bounds.Height;
            Assert.True(width > dropdown.Bounds.Width);
            Dispatcher.UIThread.RunJobs();
            Assert.True(search.IsFocused);
            var searchPoint = search.TranslatePoint(new Point(20, 15), window)!.Value;
            window.MouseDown(searchPoint, MouseButton.Left);
            window.MouseUp(searchPoint, MouseButton.Left);
            Assert.True(dropdown.IsDropDownOpen);
            window.KeyTextInput("neder");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(dropdown.ContainerFromIndex(0)!.IsVisible);
            Assert.False(dropdown.ContainerFromIndex(1)!.IsVisible);
            Assert.Equal(1, dropdown.SelectedIndex);
            Assert.Equal(width, popup.Child.Bounds.Width);
            search.Text = "English";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(width, popup.Child.Bounds.Width);
            Assert.Equal(height, popup.Child.Bounds.Height);
            Assert.True(search.IsFocused);
            Assert.True(popup.IsOpen);
            search.Text = "not a language";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(empty.IsVisible);
            Assert.Equal(width, popup.Child.Bounds.Width);
            Assert.Equal(height, popup.Child.Bounds.Height);
            Assert.Equal("No options found", empty.Text);
            search.Text = "disabled";
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(dropdown.IsDropDownOpen);
            Assert.Equal(1, dropdown.SelectedIndex);
            search.Text = "DUTCH";
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(1, dropdown.SelectedIndex);
            Assert.True(dropdown.ContainerFromIndex(0)!.IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(0, dropdown.SelectedIndex);
            Assert.False(dropdown.IsDropDownOpen);
            dropdown.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", search.Text);
            dropdown.Search = false;
            dropdown.ItemsSource = Array.Empty<string>();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(empty.IsVisible);
            dropdown.EmptyText = "Nothing here";
            Assert.Equal("Nothing here", empty.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Fields_ErrorsReplaceNotesWithoutChangingMenuIconColors()
    {
        var input = new InputField { Note = "Helpful note", Variant = InputFieldVariant.Borderless };
        var dropdown = new Dropdown
        {
            Note = "Helpful note",
            ItemsSource = new[] { new DropdownOption("Option", Geometry.Parse("M0 0 H16 V16 H0 Z")) },
            SelectedIndex = 0,
            MenuAnimationDuration = TimeSpan.Zero,
        };
        var window = new Window
        {
            Content = new StackPanel { Children = { input, dropdown } },
            Width = 320,
            Height = 250,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var inputNote = input.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "PART_Note");
            var dropdownNote = dropdown.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "PART_Note");
            Assert.True(inputNote.IsVisible);
            Assert.True(dropdownNote.IsVisible);
            input.ErrorText = dropdown.ErrorText = "Invalid";
            Assert.False(inputNote.IsVisible);
            Assert.False(dropdownNote.IsVisible);
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            var popup = dropdown.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            var optionIcon = popup.Child!.GetVisualDescendants().OfType<PathIcon>().Single(x => x.Classes.Contains("DropdownOptionIcon"));
            Assert.Equal((Color)window.FindResource("Brand400")!, Assert.IsAssignableFrom<ISolidColorBrush>(optionIcon.Foreground).Color);
            input.ErrorText = null;
            dropdown.ErrorText = "";
            Assert.True(inputNote.IsVisible);
            Assert.True(dropdownNote.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InputField_PreservesEditingAndDisplaysItsIconAndError()
    {
        var field = new InputField { Placeholder = "Example", IconData = Geometry.Parse("M0 0 H16 V16 H0 Z") };
        var window = new Window
        {
            Content = field,
            Width = 300,
            Height = 100,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal("Example", field.PlaceholderText);
            Assert.True(field.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "PART_Placeholder").IsVisible);
            Assert.True(field.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "PART_IconArea").IsVisible);
            field.Focus();
            window.KeyTextInput("Hello");
            Assert.Equal("Hello", field.Text);
            field.IsReadOnly = true;
            window.KeyTextInput(" ignored");
            Assert.Equal("Hello", field.Text);
            field.ErrorText = "Invalid value";
            window.UpdateLayout();
            Assert.True(field.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "PART_Error").IsVisible);
            field.ErrorText = "";
            field.IconData = null;
            Assert.False(field.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "PART_Error").IsVisible);
            Assert.False(field.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "PART_IconArea").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dropdown_MeasuresOffscreenOptionsAndAlignsToTheNearestEdge()
    {
        var dropdown = new Dropdown
        {
            ItemsSource = Enumerable
                .Range(0, 40)
                .Select(i => i == 39 ? "The widest option is initially outside the visible menu" : "Short")
                .ToArray(),
            Width = 180,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            MenuAnimationDuration = TimeSpan.Zero,
        };
        var window = new Window
        {
            Content = dropdown,
            Width = 800,
            Height = 400,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            var popup = dropdown.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            Assert.Equal(PlacementMode.BottomEdgeAlignedRight, popup.Placement);
            Assert.True(popup.Child!.Bounds.Width > dropdown.Bounds.Width);
            var width = popup.Child.Bounds.Width;
            dropdown.SelectedIndex = 39;
            window.UpdateLayout();
            Assert.Equal(width, popup.Child.Bounds.Width);
            dropdown.IsDropDownOpen = false;
            dropdown.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            window.UpdateLayout();
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dropdown_DisabledOptionsAreSkippedAndCannotBeActivated()
    {
        var disabled = new DropdownOption("Disabled", isEnabled: false);
        var dropdown = new Dropdown
        {
            ItemsSource = new[] { new DropdownOption("First"), disabled, new DropdownOption("Last") },
            SelectedIndex = 0,
            MenuAnimationDuration = TimeSpan.Zero,
        };
        var window = new Window { Content = dropdown };
        try
        {
            window.Show();
            window.UpdateLayout();
            dropdown.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(2, dropdown.SelectedIndex);
            window.KeyTextInput("Disabled");
            Assert.Equal(2, dropdown.SelectedIndex);
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            var container = dropdown.ContainerFromIndex(1)!;
            Assert.False(container.IsEnabled);
            Assert.False(container.Focus());
            Assert.False(
                dropdown.UpdateSelectionFromEvent(container, new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter })
            );
            Assert.Equal(2, dropdown.SelectedIndex);
            disabled.IsEnabled = true;
            Assert.True(container.IsEnabled);
            dropdown.IsDropDownOpen = false;
            dropdown.Focus();
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Equal(1, dropdown.SelectedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dropdown_OptionsCanMixIconsAndPlainText()
    {
        var iconOption = new DropdownOption("With icon", Geometry.Parse("M0 0 H16 V16 H0 Z"));
        var plainOption = new DropdownOption("Without icon");
        var dropdown = new Dropdown
        {
            ItemsSource = new[] { iconOption, plainOption },
            SelectedIndex = 0,
            MenuAnimationDuration = TimeSpan.Zero,
        };
        var window = new Window { Content = dropdown };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Same(iconOption, dropdown.SelectedItem);
            var fieldIcon = Assert.Single(
                dropdown.GetVisualDescendants().OfType<PathIcon>().Where(icon => icon.Classes.Contains("DropdownOptionIcon"))
            );
            Assert.Same(iconOption.Icon, fieldIcon.Data);
            Assert.Equal(
                (Color)Application.Current!.FindResource("Brand400")!,
                Assert.IsAssignableFrom<ISolidColorBrush>(fieldIcon.Foreground).Color
            );
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            var popup = dropdown.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            Assert.Single(
                popup.Child!.GetVisualDescendants().OfType<PathIcon>().Where(icon => icon.Classes.Contains("DropdownOptionIcon"))
            );
            Assert.Equal(
                (Color)window.FindResource("Brand300")!,
                Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<DropdownItem>(dropdown.ContainerFromIndex(0)).Foreground).Color
            );
            dropdown.IsDropDownOpen = false;
            dropdown.SelectedIndex = 1;
            window.UpdateLayout();
            Assert.Same(plainOption, dropdown.SelectedItem);
            Assert.Empty(dropdown.GetVisualDescendants().OfType<PathIcon>().Where(icon => icon.Classes.Contains("DropdownOptionIcon")));
            Assert.Contains(dropdown.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == plainOption.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Dropdown_LimitsMenuHeightAndFinishesClosingBeforeHiding()
    {
        var dropdown = new Dropdown
        {
            ItemsSource = Enumerable.Range(0, 40).Select(index => $"Option {index}").ToArray(),
            MaxDropDownHeight = 120,
            MenuAnimationDuration = TimeSpan.Zero,
        };
        var window = new Window
        {
            Content = dropdown,
            Width = 300,
            Height = 400,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            var popup = dropdown.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            var menu = Assert.IsType<Border>(popup.Child);
            menu.UpdateLayout();
            var scroll = menu.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.InRange(menu.Bounds.Height, 1, dropdown.MaxDropDownHeight);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            dropdown.IsDropDownOpen = false;
            Assert.False(popup.IsOpen);

            dropdown.MenuAnimationDuration = TimeSpan.FromMilliseconds(40);
            dropdown.IsDropDownOpen = true;
            await Task.Delay(100);
            dropdown.IsDropDownOpen = false;
            Assert.True(popup.IsOpen);
            await Task.Delay(100);
            Assert.False(popup.IsOpen);

            dropdown.IsDropDownOpen = true;
            dropdown.IsDropDownOpen = false;
            dropdown.IsDropDownOpen = true;
            await Task.Delay(100);
            Assert.True(popup.IsOpen);
            dropdown.MenuAnimationDuration = TimeSpan.Zero;
            dropdown.IsDropDownOpen = false;
            Assert.False(popup.IsOpen);

            dropdown.IsDropDownOpen = true;
            window.UpdateLayout();
            dropdown.ContainerFromIndex(2)!.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(2, dropdown.SelectedIndex);
            Assert.False(popup.IsOpen);

            dropdown.IsDropDownOpen = true;
            window.MouseDown(new Point(10, 10), MouseButton.Left);
            window.MouseUp(new Point(10, 10), MouseButton.Left);
            Assert.False(dropdown.IsDropDownOpen);
            Assert.False(popup.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dropdown_PreservesNativeKeyboardSelectionAndPopupBehavior()
    {
        var dropdown = new Dropdown { ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 0 };
        var window = new Window { Content = dropdown };
        try
        {
            window.Show();
            window.UpdateLayout();
            dropdown.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal("Second", dropdown.SelectedItem);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(dropdown.IsDropDownOpen);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(dropdown.IsDropDownOpen);
            dropdown.IsEnabled = false;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.False(dropdown.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(ToggleVariant.Checkbox)]
    [InlineData(ToggleVariant.Switch)]
    [InlineData(ToggleVariant.Radio)]
    public void ToggleCheckbox_VariantsPreserveContentAndReleaseActivation(ToggleVariant variant)
    {
        var content = new TextBlock { Text = "Toggle" };
        var toggle = new ToggleCheckBox { Content = content, Variant = variant };
        var window = new Window { Content = toggle };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Contains(content, toggle.GetVisualDescendants());
            toggle.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.False(toggle.IsChecked == true);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(toggle.IsChecked);
            toggle.Variant = ToggleVariant.Switch;
            window.UpdateLayout();
            Assert.True(toggle.IsChecked);
            Assert.Same(content, toggle.Content);
            toggle.IsEnabled = false;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(toggle.IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ToggleTransitions_TrackTheAnimationDurationResource()
    {
        var resources = Application.Current!.Resources;
        var durationKey = WindowAppearance.ControlAnimationDurationResourceKey;
        var previous = resources[durationKey];
        var toggle = new ToggleCheckBox { Variant = ToggleVariant.Switch };
        var window = new Window { Content = toggle };
        try
        {
            window.Show();
            window.UpdateLayout();
            var transitions = toggle
                .GetVisualDescendants()
                .OfType<Animatable>()
                .SelectMany(control => control.Transitions ?? [])
                .Cast<TransitionBase>()
                .ToList();
            Assert.NotEmpty(transitions);
            resources[durationKey] = TimeSpan.Zero;
            Assert.All(transitions, transition => Assert.Equal(TimeSpan.Zero, transition.Duration));
            resources[durationKey] = TimeSpan.FromMilliseconds(120);
            Assert.All(transitions, transition => Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration));
        }
        finally
        {
            window.Close();
            resources[durationKey] = previous;
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
    public void InputField_PreservesCallerBindingAndDerivesErrorsFromText()
    {
        var model = new InputModel();
        var field = new InputField { DataContext = model, ErrorText = "Invalid value" };
        field.Bind(InputField.TextProperty, new Binding(nameof(InputModel.Value)));
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
            var input = field;
            Assert.Same(model, field.DataContext);
            Assert.Equal("Initial", input.Text);
            Assert.True(field.HasError);
            input.SetCurrentValue(TextBox.TextProperty, "Edited");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Edited", model.Value);
            model.Value = "Updated";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Updated", input.Text);
            field.ErrorText = null;
            Assert.False(field.HasError);
            foreach (var silent in new[] { "", "   " })
            {
                field.ErrorText = silent;
                window.UpdateLayout();
                Assert.True(field.HasError);
                Assert.False(field.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "PART_Error").IsVisible);
            }
            field.ErrorText = "Duplicate friend code";
            Assert.True(field.HasError);
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
        var field = dialog.FindControl<InputField>("InputField")!;
        var submit = dialog.FindControl<ActionButton>("SubmitButton")!;
        Assert.True(field.HasError);
        Assert.Equal("Already in your list", field.ErrorText);
        Assert.False(submit.IsEnabled);
        dialog.SetInitialText("unique");
        Assert.False(field.HasError);
        Assert.True(submit.IsEnabled);
    }

    [AvaloniaFact]
    public void LinkVariant_UsesNativeKeyboardClickAndDisabledBehavior()
    {
        var button = new ActionButton
        {
            Text = "Open",
            Variant = ActionButtonVariant.Link,
            Tone = ActionButtonTone.Brand,
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
        foreach (
            Control button in new Control[]
            {
                new SidebarRadioButton(),
                new DropdownItem { Content = "Option" },
                new ActionButton { Text = "Example" },
            }
        )
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
