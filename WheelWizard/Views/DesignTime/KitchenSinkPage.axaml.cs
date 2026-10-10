using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell.Navigation;
using Spinner = WheelWizard.Views.Components.Spinner;

namespace WheelWizard.Views.DesignTime;

public partial class KitchenSinkPage : UserControl, ILockedSidebarPage
{
    public KitchenSinkPage()
    {
        InitializeComponent();
        ComponentSelector.ItemsSource = new[]
        {
            "Checkbox",
            "Radio button",
            "Dropdown",
            "Input field",
            "Badge",
            "Button",
            "Tooltip",
            "Spinner",
            "Segmented control",
            "Split button",
        };
        ComponentSelector.SelectedIndex = 0;
    }

    private void ComponentSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ToggleExamples is null)
            return;

        if (ComponentSelector.SelectedIndex == 2)
        {
            ToggleExamples.Children.Clear();
            ToggleExamples.Children.Add(CreateDropdownExample());
            return;
        }

        if (ComponentSelector.SelectedIndex == 3)
        {
            ToggleExamples.Children.Clear();
            ToggleExamples.Children.Add(CreateInputExample());
            return;
        }
        if (ComponentSelector.SelectedIndex == 4)
        {
            ToggleExamples.Children.Clear();
            ToggleExamples.Children.Add(CreateBadgeExample());
            return;
        }
        if (ComponentSelector.SelectedIndex == 5)
        {
            ToggleExamples.Children.Clear();
            ToggleExamples.Children.Add(CreateButtonExample());
            return;
        }
        if (ComponentSelector.SelectedIndex == 6)
        {
            ToggleExamples.Children.Clear();
            ToggleExamples.Children.Add(CreateTooltipExample());
            return;
        }
        if (ComponentSelector.SelectedIndex is 7 or 8)
        {
            ToggleExamples.Children.Clear();
            ToggleExamples.Children.Add(ComponentSelector.SelectedIndex == 7 ? CreateSpinnerExample() : CreateSegmentedExample());
            return;
        }
        if (ComponentSelector.SelectedIndex == 9)
        {
            ToggleExamples.Children.Clear();
            ToggleExamples.Children.Add(CreateSplitButtonExample());
            return;
        }
        var radio = ComponentSelector.SelectedIndex == 1;
        ToggleButton preview = radio
            ? new ToggleRadioButton { Content = "Example radio button", GroupName = "KitchenSinkPreviews" }
            : new ToggleCheckBox { Content = "Example checkbox" };
        ToggleButton second = radio ? new ToggleRadioButton { GroupName = "KitchenSinkPreviews" } : new ToggleCheckBox();
        second.Content = "Second button preview";
        second.Bind(ToggleCheckBox.VariantProperty, new Binding(nameof(ToggleCheckBox.Variant)) { Source = preview });
        ToggleExamples.Children.Clear();
        ToggleExamples.Children.Add(CreateExample(radio ? "Radio button" : "Checkbox", preview, second));
    }

    private static StackPanel CreateSpinnerExample()
    {
        var preview = new Spinner();
        preview.Bind(HeightProperty, new Binding(nameof(preview.Width)) { Source = preview });
        var size = new Dropdown { ItemsSource = new double[] { 16, 24, 32, 48 } };
        size.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(preview.Width)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        return CreateComponentExample("Spinner", preview, CreateRow(Label("Width"), Label("24"), size));
    }

    private static StackPanel CreateSplitButtonExample()
    {
        var preview = new WheelWizard.Views.Components.SplitButton { Text = "Main action", HorizontalAlignment = HorizontalAlignment.Left };
        var feedback = Label("Choose an action");
        preview.Click += (_, _) => feedback.Text = "Main action clicked";
        var menu = new MenuFlyout();
        var action = new SplitButtonItem
        {
            Header = "Another action",
            IconData = (Geometry)Avalonia.Application.Current!.FindResource("Search")!,
        };
        action.Click += (_, _) => feedback.Text = "Menu action clicked";
        menu.Items.Add(action);
        menu.Items.Add(new SplitButtonItem { Header = "Unavailable action", IsEnabled = false });
        preview.Flyout = menu;
        var text = new InputField();
        text.Bind(TextBox.TextProperty, new Binding(nameof(preview.Text)) { Source = preview, Mode = BindingMode.TwoWay });
        var tone = new Dropdown { ItemsSource = Enum.GetValues<ActionButtonTone>() };
        tone.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(preview.Tone)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        var size = new Dropdown { ItemsSource = Enum.GetValues<ButtonSize>() };
        size.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(preview.Size)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        var enabled = new ToggleCheckBox { Variant = ToggleVariant.Switch };
        enabled.Bind(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(preview.IsEnabled)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        var icons = CreateIconPicker();
        icons.Bind(
            SelectingItemsControl.SelectedValueProperty,
            new Binding(nameof(preview.IconData)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        icons.SelectedIndex = 0;
        return CreateComponentExample(
            "Split button",
            new StackPanel { Spacing = 8, Children = { preview, feedback } },
            CreateRow(Label("Text"), Label("Main action"), text),
            CreateRow(Label("Tone"), Label("Primary"), tone),
            CreateRow(Label("Size"), Label("Regular"), size),
            CreateRow(Label("IconData"), Label("None"), icons),
            CreateRow(Label("IsEnabled"), Label("True"), enabled)
        );
    }

    private static StackPanel CreateSegmentedExample()
    {
        var preview = new SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left, SelectedIndex = 0 };
        preview.Items.Add(new SegmentOption { Text = "Grid", IconData = (Geometry)Avalonia.Application.Current!.FindResource("Grid2x2")! });
        preview.Items.Add(new SegmentOption { Text = "A longer option" });
        var iconOnly = new SegmentOption { IconData = (Geometry)Avalonia.Application.Current!.FindResource("Search")! };
        AutomationProperties.SetName(iconOnly, "Search");
        ToolTip.SetTip(iconOnly, "Search");
        preview.Items.Add(iconOnly);
        var selected = new Dropdown { ItemsSource = new[] { 0, 1, 2 } };
        selected.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(preview.SelectedIndex)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        var enabled = new ToggleCheckBox { Variant = ToggleVariant.Switch };
        enabled.Bind(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(preview.IsEnabled)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        var border = new ToggleCheckBox { Variant = ToggleVariant.Switch };
        border.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(preview.HasBorder)) { Source = preview, Mode = BindingMode.TwoWay });
        var orientation = new Dropdown { ItemsSource = Enum.GetValues<Orientation>() };
        orientation.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(preview.Orientation)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        var variant = new Dropdown { ItemsSource = Enum.GetValues<SegmentedControlVariant>() };
        variant.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(preview.Variant)) { Source = preview, Mode = BindingMode.TwoWay }
        );
        return CreateComponentExample(
            "Segmented control",
            preview,
            CreateRow(Label("SelectedIndex"), Label("0"), selected),
            CreateRow(Label("IsEnabled"), Label("True"), enabled),
            CreateRow(Label("HasBorder"), Label("True"), border),
            CreateRow(Label("Orientation"), Label("Horizontal"), orientation),
            CreateRow(Label("Variant"), Label("Dark"), variant)
        );
    }

    private static StackPanel CreateComponentExample(string title, Control preview, params Control[] attributes)
    {
        var header = CreateRow(Label("Attribute"), Label("Initial value"), Label("Value"));
        header.Classes.Add("KitchenSinkAttributeHeader");
        header.BorderThickness = new(0);
        var rows = new StackPanel { Children = { header } };
        foreach (var attribute in attributes)
            rows.Children.Add(attribute);
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new FormFieldLabel { Text = title },
                new Border
                {
                    Child = preview,
                    MinHeight = 64,
                    Padding = new(12),
                },
                new Border { Classes = { "KitchenSinkAttributeTable" }, Child = rows },
            },
        };
    }

    private static StackPanel CreateTooltipExample()
    {
        var tip = new HintTooltip { Text = "A tooltip can be attached to any control." };
        var target = new ActionButton { Text = "Hover for a tooltip", HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(target, tip);
        ToolTip.SetPlacement(target, PlacementMode.Top);
        var rows = new StackPanel();
        var header = CreateRow(Label("Attribute"), Label("Initial value"), Label("Value"));
        header.Classes.Add("KitchenSinkAttributeHeader");
        header.BorderThickness = new(0);
        rows.Children.Add(header);
        var text = new InputField();
        text.Bind(TextBox.TextProperty, new Binding(nameof(tip.Text)) { Source = tip, Mode = BindingMode.TwoWay });
        rows.Children.Add(CreateRow(Label("Text"), Label(tip.Text!), text));
        var width = new Dropdown { ItemsSource = new double[] { 180, 280, 360 } };
        width.Bind(
            SelectingItemsControl.SelectedItemProperty,
            new Binding(nameof(tip.MaxWidth)) { Source = tip, Mode = BindingMode.TwoWay }
        );
        rows.Children.Add(CreateRow(Label("MaxWidth"), Label("280"), width));
        // Custom and AnchorAndGravity require positioning configuration beyond a placement choice.
        var placement = new Dropdown
        {
            ItemsSource = Enum.GetValues<PlacementMode>()
                .Where(mode => mode is not (PlacementMode.Custom or PlacementMode.AnchorAndGravity)),
        };
        placement.SelectedItem = PlacementMode.Top;
        placement.SelectionChanged += (_, _) =>
        {
            if (placement.SelectedItem is PlacementMode mode)
                ToolTip.SetPlacement(target, mode);
        };
        rows.Children.Add(CreateRow(Label("Placement"), Label("Top"), placement));
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new FormFieldLabel { Text = "Tooltip" },
                new Border
                {
                    Child = target,
                    MinHeight = 64,
                    Padding = new(12),
                },
                new Border { Classes = { "KitchenSinkAttributeTable" }, Child = rows },
            },
        };
    }

    private static StackPanel CreateButtonExample()
    {
        var preview = new ActionButton { Text = "Example button", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(preview, "Button preview");
        var feedback = Label("Clicked 0 times");
        var clicks = 0;
        preview.Click += (_, _) => feedback.Text = $"Clicked {++clicks} times";
        var rows = new StackPanel();
        var header = CreateRow(Label("Attribute"), Label("Initial value"), Label("Value"));
        header.BorderThickness = new(0);
        header.Classes.Add("KitchenSinkAttributeHeader");
        rows.Children.Add(header);
        AddAttribute(nameof(preview.Text), preview.Text!, new InputField(), TextBox.TextProperty);
        AddAttribute(
            nameof(preview.Variant),
            "Button",
            new Dropdown { ItemsSource = Enum.GetValues<ActionButtonVariant>() },
            SelectingItemsControl.SelectedItemProperty
        );
        AddAttribute(
            nameof(preview.Tone),
            "Primary",
            new Dropdown { ItemsSource = Enum.GetValues<ActionButtonTone>() },
            SelectingItemsControl.SelectedItemProperty
        );
        AddAttribute(
            nameof(preview.Size),
            "Regular",
            new Dropdown { ItemsSource = Enum.GetValues<ButtonSize>() },
            SelectingItemsControl.SelectedItemProperty
        );
        AddAttribute(
            nameof(preview.IsLoading),
            "False",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        AddAttribute(
            nameof(preview.IsCircular),
            "False",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        var icons = CreateIconPicker();
        AddAttribute(nameof(preview.IconData), "None", icons, SelectingItemsControl.SelectedValueProperty);
        icons.SelectedIndex = 0;
        AddAttribute(
            nameof(preview.IsIconLeft),
            "True",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        AddAttribute(
            nameof(preview.IsEnabled),
            "True",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new FormFieldLabel { Text = "Button" },
                new Border
                {
                    Child = new StackPanel { Spacing = 10, Children = { preview, feedback } },
                    MinHeight = 64,
                    Padding = new(12),
                },
                new Border { Classes = { "KitchenSinkAttributeTable" }, Child = rows },
            },
        };
        void AddAttribute(string name, string initial, Control editor, Avalonia.AvaloniaProperty property)
        {
            editor.Bind(property, new Binding(name) { Source = preview, Mode = BindingMode.TwoWay });
            AutomationProperties.SetName(editor, $"Button {name}");
            rows.Children.Add(CreateRow(Label(name), Label(initial), editor));
        }
    }

    private static Dropdown CreateIconPicker()
    {
        var icons = new List<DropdownOption> { new("None") };
        void AddIcons(Avalonia.Controls.IResourceProvider provider)
        {
            if (provider is Avalonia.Markup.Xaml.Styling.ResourceInclude include)
                AddIcons(include.Loaded);
            else if (provider is ResourceDictionary dictionary)
            {
                // Resolve lazy XAML resources before checking their type.
                foreach (var key in dictionary.Keys)
                    if (key is string name && dictionary.TryGetResource(key, null, out var value) && value is Geometry geometry)
                        icons.Add(new DropdownOption(name, geometry));
                foreach (var merged in dictionary.MergedDictionaries)
                    AddIcons(merged);
            }
        }
        AddIcons(Avalonia.Application.Current!.Resources);
        icons = icons.Take(1).Concat(icons.Skip(1).OrderBy(option => option.Text)).ToList();
        return new Dropdown
        {
            ItemsSource = icons,
            Search = true,
            SelectedValueBinding = new Binding(nameof(DropdownOption.Icon)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static StackPanel CreateBadgeExample()
    {
        var preview = new StatusBadge
        {
            Text = "Example badge",
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(preview, "Badge preview");
        var rows = new StackPanel();
        var header = CreateRow(Label("Attribute"), Label("Initial value"), Label("Value"));
        header.BorderThickness = new(0);
        header.Classes.Add("KitchenSinkAttributeHeader");
        rows.Children.Add(header);
        AddAttribute(nameof(preview.Text), preview.Text!, new InputField(), TextBox.TextProperty);
        AddAttribute(
            nameof(preview.Variant),
            "Gray",
            new Dropdown { ItemsSource = Enum.GetValues<StatusVariant>() },
            SelectingItemsControl.SelectedItemProperty
        );
        AddAttribute(
            nameof(preview.Height),
            "24",
            new Dropdown { ItemsSource = new double[] { 24, 28, 32 } },
            SelectingItemsControl.SelectedItemProperty
        );
        var icons = CreateIconPicker();
        AddAttribute(nameof(preview.IconData), "None", icons, SelectingItemsControl.SelectedValueProperty);
        icons.SelectedIndex = 0;
        AddAttribute(nameof(preview.TipText), "", new InputField(), TextBox.TextProperty);
        AddAttribute(
            nameof(preview.IsEnabled),
            "True",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new FormFieldLabel { Text = "Badge" },
                new Border
                {
                    Child = preview,
                    MinHeight = 64,
                    Padding = new(12),
                },
                new Border { Classes = { "KitchenSinkAttributeTable" }, Child = rows },
            },
        };
        void AddAttribute(string name, string initial, Control editor, Avalonia.AvaloniaProperty property)
        {
            editor.Bind(property, new Binding(name) { Source = preview, Mode = BindingMode.TwoWay });
            AutomationProperties.SetName(editor, $"Badge {name}");
            rows.Children.Add(CreateRow(Label(name), Label(initial), editor));
        }
    }

    private static StackPanel CreateInputExample()
    {
        var preview = new InputField
        {
            Placeholder = "Enter some text",
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(preview, "Input field preview");
        var rows = new StackPanel();
        var header = CreateRow(Label("Attribute"), Label("Initial value"), Label("Value"));
        header.BorderThickness = new(0);
        header.Classes.Add("KitchenSinkAttributeHeader");
        rows.Children.Add(header);
        AddAttribute(nameof(preview.Text), "", new InputField(), TextBox.TextProperty);
        AddAttribute(nameof(preview.Placeholder), preview.Placeholder, new InputField(), TextBox.TextProperty);
        var iconPicker = CreateIconPicker();
        AddAttribute(nameof(preview.IconData), "None", iconPicker, SelectingItemsControl.SelectedValueProperty);
        iconPicker.SelectedIndex = 0;
        AddAttribute(nameof(preview.Note), "", new InputField(), TextBox.TextProperty);
        AddAttribute(
            nameof(preview.Variant),
            "Bordered",
            new Dropdown { ItemsSource = Enum.GetValues<InputFieldVariant>() },
            SelectingItemsControl.SelectedItemProperty
        );
        AddAttribute(nameof(preview.ErrorText), "", new InputField(), TextBox.TextProperty);
        AddAttribute(
            nameof(preview.IsEnabled),
            "True",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        AddAttribute(
            nameof(preview.IsReadOnly),
            "False",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new FormFieldLabel { Text = "Input field" },
                new Border
                {
                    Child = preview,
                    MinHeight = 64,
                    Padding = new(12),
                },
                new Border { Classes = { "KitchenSinkAttributeTable" }, Child = rows },
            },
        };
        void AddAttribute(string name, string? initialValue, Control editor, Avalonia.AvaloniaProperty property)
        {
            editor.Bind(property, new Binding(name) { Source = preview, Mode = BindingMode.TwoWay });
            AutomationProperties.SetName(editor, $"Input field {name}");
            rows.Children.Add(CreateRow(Label(name), Label(initialValue ?? ""), editor));
        }
    }

    private static StackPanel CreateDropdownExample()
    {
        var preview = new Dropdown
        {
            ItemsSource = new[]
            {
                new DropdownOption("First option", Geometry.Parse("M2 8 L8 2 L14 8 L8 14 Z")),
                new DropdownOption("Second option"),
                new DropdownOption("Third option", Geometry.Parse("M2 2 H14 V14 H2 Z")),
                new DropdownOption("Disabled option", isEnabled: false),
            },
            PlaceholderText = "Choose an option",
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(preview, "Dropdown preview");
        var rows = new StackPanel();
        var header = CreateRow(Label("Attribute"), Label("Initial value"), Label("Value"));
        header.BorderThickness = new(0);
        header.Classes.Add("KitchenSinkAttributeHeader");
        rows.Children.Add(header);

        var placeholder = new InputField();
        AddAttribute(nameof(preview.PlaceholderText), preview.PlaceholderText, placeholder, TextBox.TextProperty);
        var selected = new Dropdown { ItemsSource = new[] { -1, 0, 1, 2 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        AddAttribute(nameof(preview.SelectedIndex), "-1", selected, SelectingItemsControl.SelectedItemProperty);
        var enabled = new ToggleCheckBox { Variant = ToggleVariant.Switch };
        AddAttribute(nameof(preview.IsEnabled), "True", enabled, ToggleButton.IsCheckedProperty);
        var variant = new Dropdown { ItemsSource = Enum.GetValues<DropdownVariant>(), HorizontalAlignment = HorizontalAlignment.Stretch };
        AddAttribute(nameof(preview.Variant), "Bordered", variant, SelectingItemsControl.SelectedItemProperty);
        AddAttribute(nameof(preview.Note), "", new InputField(), TextBox.TextProperty);
        AddAttribute(
            nameof(preview.Search),
            "False",
            new ToggleCheckBox { Variant = ToggleVariant.Switch },
            ToggleButton.IsCheckedProperty
        );
        AddAttribute(nameof(preview.SearchPlaceholder), preview.SearchPlaceholder ?? "Search...", new InputField(), TextBox.TextProperty);
        AddAttribute(nameof(preview.EmptyText), preview.EmptyText ?? "No options found", new InputField(), TextBox.TextProperty);
        var error = new InputField();
        AddAttribute(nameof(preview.ErrorText), "", error, TextBox.TextProperty);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new FormFieldLabel { Text = "Dropdown" },
                new Border
                {
                    Child = preview,
                    MinHeight = 64,
                    Padding = new(12),
                },
                new Border { Classes = { "KitchenSinkAttributeTable" }, Child = rows },
            },
        };

        void AddAttribute(string name, string initialValue, Control editor, Avalonia.AvaloniaProperty editorProperty)
        {
            editor.Bind(editorProperty, new Binding(name) { Source = preview, Mode = BindingMode.TwoWay });
            AutomationProperties.SetName(editor, $"Dropdown {name}");
            rows.Children.Add(CreateRow(Label(name), Label(initialValue), editor));
        }
    }

    private static StackPanel CreateExample(string title, ToggleButton preview, ToggleButton second)
    {
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        preview.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(preview, $"{title} preview");

        var rows = new StackPanel();
        var header = CreateRow(Label("Attribute"), Label("Initial value"), Label("Value"));
        header.BorderThickness = new(0);
        header.Classes.Add("KitchenSinkAttributeHeader");
        rows.Children.Add(header);

        var content = new InputField();
        content.Bind(TextBox.TextProperty, Property(nameof(preview.Content)));
        AddAttribute(nameof(preview.Content), preview.Content?.ToString() ?? "", content);

        var isChecked = new ToggleCheckBox { Variant = ToggleVariant.Switch };
        isChecked.Bind(ToggleButton.IsCheckedProperty, Property(nameof(preview.IsChecked)));
        AddAttribute(nameof(preview.IsChecked), "False", isChecked);

        var isEnabled = new ToggleCheckBox { Variant = ToggleVariant.Switch };
        isEnabled.Bind(ToggleButton.IsCheckedProperty, Property(nameof(preview.IsEnabled)));
        AddAttribute(nameof(preview.IsEnabled), "True", isEnabled);

        var variant = new Dropdown { ItemsSource = Enum.GetValues<ToggleVariant>(), HorizontalAlignment = HorizontalAlignment.Stretch };
        variant.Bind(SelectingItemsControl.SelectedItemProperty, Property(nameof(ToggleCheckBox.Variant)));
        AddAttribute(nameof(ToggleCheckBox.Variant), preview.GetValue(ToggleCheckBox.VariantProperty).ToString(), variant);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new FormFieldLabel { Text = title },
                new Border
                {
                    Child = new StackPanel { Spacing = 12, Children = { preview, second } },
                    MinHeight = 64,
                    Padding = new(12),
                },
                new Border { Classes = { "KitchenSinkAttributeTable" }, Child = rows },
            },
        };

        Binding Property(string name) => new(name) { Source = preview, Mode = BindingMode.TwoWay };

        void AddAttribute(string name, string initialValue, Control editor)
        {
            AutomationProperties.SetName(editor, $"{title} {name}");
            rows.Children.Add(CreateRow(Label(name), Label(initialValue), editor));
        }
    }

    private static Border CreateRow(Control attribute, Control initialValue, Control editor)
    {
        var cells = new Grid { ColumnDefinitions = new("2*,*,2*"), ColumnSpacing = 12 };
        cells.Children.Add(attribute);
        Grid.SetColumn(initialValue, 1);
        cells.Children.Add(initialValue);
        Grid.SetColumn(editor, 2);
        cells.Children.Add(editor);
        return new Border { Classes = { "KitchenSinkAttributeRow" }, Child = cells };
    }

    private static TextBlock Label(string text) =>
        new()
        {
            Classes = { "TinyText" },
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private void BackgroundSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (ToggleCard is null)
            return;

        ToggleCard.Classes.Set("BlockBackground900", ((ToggleCheckBox)sender!).IsChecked == true);
    }
}
