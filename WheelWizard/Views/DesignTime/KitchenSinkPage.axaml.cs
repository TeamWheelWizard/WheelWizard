using System.Collections;
using Avalonia;
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
    private static readonly (string Name, Func<Control> Create)[] Components =
    [
        ("Checkbox", () => CreateToggleExample(radio: false)),
        ("Radio button", () => CreateToggleExample(radio: true)),
        ("Dropdown", CreateDropdownExample),
        ("Input field", CreateInputExample),
        ("Badge", CreateBadgeExample),
        ("Button", CreateButtonExample),
        ("Tooltip", CreateTooltipExample),
        ("Spinner", CreateSpinnerExample),
        ("Segmented control", CreateSegmentedExample),
        ("Split button", CreateSplitButtonExample),
    ];

    public KitchenSinkPage()
    {
        InitializeComponent();
        ComponentSelector.ItemsSource = Components.Select(component => component.Name).ToArray();
        ComponentSelector.SelectedIndex = 0;
    }

    private void ComponentSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ToggleExamples is null || ComponentSelector.SelectedIndex < 0)
            return;
        ToggleExamples.Children.Clear();
        ToggleExamples.Children.Add(Components[ComponentSelector.SelectedIndex].Create());
    }

    private static Control CreateToggleExample(bool radio)
    {
        var title = radio ? "Radio button" : "Checkbox";
        ToggleButton preview = radio
            ? new ToggleRadioButton { Content = "Example radio button", GroupName = "KitchenSinkPreviews" }
            : new ToggleCheckBox { Content = "Example checkbox" };
        ToggleButton second = radio ? new ToggleRadioButton { GroupName = "KitchenSinkPreviews" } : new ToggleCheckBox();
        second.Content = "Second button preview";
        second.Bind(ToggleCheckBox.VariantProperty, new Binding(nameof(ToggleCheckBox.Variant)) { Source = preview });
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        preview.VerticalAlignment = VerticalAlignment.Center;

        return new Example(title, preview, title, new StackPanel { Spacing = 12, Children = { preview, second } })
            .Text(nameof(preview.Content), preview.Content?.ToString())
            .Switch(nameof(preview.IsChecked), "False")
            .Switch(nameof(preview.IsEnabled), "True")
            .Choice(
                nameof(ToggleCheckBox.Variant),
                preview.GetValue(ToggleCheckBox.VariantProperty).ToString(),
                Enum.GetValues<ToggleVariant>()
            )
            .Build();
    }

    private static Control CreateDropdownExample()
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
        return new Example("Dropdown", preview, "Dropdown")
            .Text(nameof(preview.PlaceholderText), preview.PlaceholderText)
            .Choice(nameof(preview.SelectedIndex), "-1", new[] { -1, 0, 1, 2 })
            .Switch(nameof(preview.IsEnabled), "True")
            .Choice(nameof(preview.Variant), "Bordered", Enum.GetValues<DropdownVariant>())
            .Text(nameof(preview.Note), "")
            .Switch(nameof(preview.Search), "False")
            .Text(nameof(preview.SearchPlaceholder), preview.SearchPlaceholder ?? "Search...")
            .Text(nameof(preview.EmptyText), preview.EmptyText ?? "No options found")
            .Text(nameof(preview.ErrorText), "")
            .Build();
    }

    private static Control CreateInputExample()
    {
        var preview = new InputField
        {
            Placeholder = "Enter some text",
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return new Example("Input field", preview, "Input field")
            .Text(nameof(preview.Text), "")
            .Text(nameof(preview.Placeholder), preview.Placeholder)
            .Icon(nameof(preview.IconData))
            .Text(nameof(preview.Note), "")
            .Choice(nameof(preview.Variant), "Bordered", Enum.GetValues<InputFieldVariant>())
            .Text(nameof(preview.ErrorText), "")
            .Switch(nameof(preview.IsEnabled), "True")
            .Switch(nameof(preview.IsReadOnly), "False")
            .Build();
    }

    private static Control CreateBadgeExample()
    {
        var preview = new StatusBadge
        {
            Text = "Example badge",
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Example("Badge", preview, "Badge")
            .Text(nameof(preview.Text), preview.Text)
            .Choice(nameof(preview.Variant), "Gray", Enum.GetValues<StatusVariant>())
            .Choice(nameof(preview.Height), "24", new double[] { 24, 28, 32 })
            .Icon(nameof(preview.IconData))
            .Text(nameof(preview.TipText), "")
            .Switch(nameof(preview.IsEnabled), "True")
            .Build();
    }

    private static Control CreateButtonExample()
    {
        var preview = new ActionButton { Text = "Example button", HorizontalAlignment = HorizontalAlignment.Left };
        var feedback = Label("Clicked 0 times");
        var clicks = 0;
        preview.Click += (_, _) => feedback.Text = $"Clicked {++clicks} times";
        return new Example("Button", preview, "Button", new StackPanel { Spacing = 10, Children = { preview, feedback } })
            .Text(nameof(preview.Text), preview.Text)
            .Choice(nameof(preview.Variant), "Button", Enum.GetValues<ActionButtonVariant>())
            .Choice(nameof(preview.Tone), "Primary", Enum.GetValues<ActionButtonTone>())
            .Choice(nameof(preview.Size), "Regular", Enum.GetValues<ButtonSize>())
            .Switch(nameof(preview.IsLoading), "False")
            .Switch(nameof(preview.IsCircular), "False")
            .Icon(nameof(preview.IconData))
            .Switch(nameof(preview.IsIconLeft), "True")
            .Switch(nameof(preview.IsEnabled), "True")
            .Build();
    }

    private static Control CreateTooltipExample()
    {
        var tip = new HintTooltip { Text = "A tooltip can be attached to any control." };
        var target = new ActionButton { Text = "Hover for a tooltip", HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(target, tip);
        ToolTip.SetPlacement(target, PlacementMode.Top);
        // Custom and AnchorAndGravity require positioning configuration beyond a placement choice.
        var placement = new Dropdown
        {
            ItemsSource = Enum.GetValues<PlacementMode>()
                .Where(mode => mode is not (PlacementMode.Custom or PlacementMode.AnchorAndGravity)),
            SelectedItem = PlacementMode.Top,
        };
        placement.SelectionChanged += (_, _) =>
        {
            if (placement.SelectedItem is PlacementMode mode)
                ToolTip.SetPlacement(target, mode);
        };
        return new Example("Tooltip", tip, content: target)
            .Text(nameof(tip.Text), tip.Text)
            .Choice(nameof(tip.MaxWidth), "280", new double[] { 180, 280, 360 })
            .Row("Placement", "Top", placement)
            .Build();
    }

    private static Control CreateSpinnerExample()
    {
        var preview = new Spinner();
        preview.Bind(HeightProperty, new Binding(nameof(preview.Width)) { Source = preview });
        return new Example("Spinner", preview).Choice(nameof(preview.Width), "24", new double[] { 16, 24, 32, 48 }).Build();
    }

    private static Control CreateSegmentedExample()
    {
        var preview = new SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left, SelectedIndex = 0 };
        preview.Items.Add(new SegmentOption { Text = "Grid", IconData = Icon("Grid2x2") });
        preview.Items.Add(new SegmentOption { Text = "A longer option" });
        var iconOnly = new SegmentOption { IconData = Icon("Search") };
        AutomationProperties.SetName(iconOnly, "Search");
        ToolTip.SetTip(iconOnly, "Search");
        preview.Items.Add(iconOnly);
        return new Example("Segmented control", preview)
            .Choice(nameof(preview.SelectedIndex), "0", new[] { 0, 1, 2 })
            .Switch(nameof(preview.IsEnabled), "True")
            .Switch(nameof(preview.HasBorder), "True")
            .Choice(nameof(preview.Orientation), "Horizontal", Enum.GetValues<Orientation>())
            .Choice(nameof(preview.Variant), "Dark", Enum.GetValues<SegmentedControlVariant>())
            .Build();
    }

    private static Control CreateSplitButtonExample()
    {
        var preview = new WheelWizard.Views.Components.SplitButton { Text = "Main action", HorizontalAlignment = HorizontalAlignment.Left };
        var feedback = Label("Choose an action");
        preview.Click += (_, _) => feedback.Text = "Main action clicked";
        var action = new SplitButtonItem { Header = "Another action", IconData = Icon("Search") };
        action.Click += (_, _) => feedback.Text = "Menu action clicked";
        preview.Flyout = new MenuFlyout
        {
            Items =
            {
                action,
                new SplitButtonItem { Header = "Unavailable action", IsEnabled = false },
            },
        };
        return new Example("Split button", preview, content: new StackPanel { Spacing = 8, Children = { preview, feedback } })
            .Text(nameof(preview.Text), "Main action")
            .Choice(nameof(preview.Tone), "Primary", Enum.GetValues<ActionButtonTone>())
            .Choice(nameof(preview.Size), "Regular", Enum.GetValues<ButtonSize>())
            .Icon(nameof(preview.IconData))
            .Switch(nameof(preview.IsEnabled), "True")
            .Build();
    }

    private static Geometry Icon(string key) => (Geometry)Application.Current!.FindResource(key)!;

    private static Dropdown CreateIconPicker()
    {
        var icons = new List<DropdownOption>();
        void AddIcons(IResourceProvider provider)
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
        AddIcons(Application.Current!.Resources);
        return new Dropdown
        {
            ItemsSource = icons.OrderBy(option => option.Text).Prepend(new DropdownOption("None")).ToList(),
            Search = true,
            SelectedValueBinding = new Binding(nameof(DropdownOption.Icon)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
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

    /// <summary>
    /// A component preview above a table of its attributes, each with an editor bound two-way to <paramref name="target"/>.
    /// With an <paramref name="automationName"/> the preview and editors are named for UI automation ("Button Text").
    /// </summary>
    private sealed class Example(string title, AvaloniaObject target, string? automationName = null, Control? content = null)
    {
        private readonly StackPanel _rows = new() { Children = { Header() } };

        public Example Text(string name, string? initial) => Bound(name, initial, new InputField(), TextBox.TextProperty);

        public Example Switch(string name, string initial) =>
            Bound(name, initial, new ToggleCheckBox { Variant = ToggleVariant.Switch }, ToggleButton.IsCheckedProperty);

        public Example Choice(string name, string? initial, IEnumerable items) =>
            Bound(
                name,
                initial,
                new Dropdown { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch },
                SelectingItemsControl.SelectedItemProperty
            );

        public Example Icon(string name)
        {
            var icons = CreateIconPicker();
            Bound(name, "None", icons, SelectingItemsControl.SelectedValueProperty);
            icons.SelectedIndex = 0;
            return this;
        }

        public Example Row(string name, string? initial, Control editor)
        {
            if (automationName is not null)
                AutomationProperties.SetName(editor, $"{automationName} {name}");
            _rows.Children.Add(Cells(Label(name), Label(initial ?? ""), editor));
            return this;
        }

        public StackPanel Build()
        {
            if (automationName is not null && target is Control preview)
                AutomationProperties.SetName(preview, $"{automationName} preview");
            return new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new FormFieldLabel { Text = title },
                    new Border
                    {
                        Child = content ?? (Control)target,
                        MinHeight = 64,
                        Padding = new(12),
                    },
                    new Border { Classes = { "KitchenSinkAttributeTable" }, Child = _rows },
                },
            };
        }

        private Example Bound(string name, string? initial, Control editor, AvaloniaProperty editorProperty)
        {
            editor.Bind(editorProperty, new Binding(name) { Source = target, Mode = BindingMode.TwoWay });
            return Row(name, initial, editor);
        }

        private static Border Header()
        {
            var header = Cells(Label("Attribute"), Label("Initial value"), Label("Value"));
            header.Classes.Add("KitchenSinkAttributeHeader");
            header.BorderThickness = new(0);
            return header;
        }

        private static Border Cells(Control attribute, Control initialValue, Control editor)
        {
            var cells = new Grid { ColumnDefinitions = new("2*,*,2*"), ColumnSpacing = 12 };
            cells.Children.Add(attribute);
            Grid.SetColumn(initialValue, 1);
            cells.Children.Add(initialValue);
            Grid.SetColumn(editor, 2);
            cells.Children.Add(editor);
            return new Border { Classes = { "KitchenSinkAttributeRow" }, Child = cells };
        }
    }
}
