using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WheelWizard.Views.Components;

public class Dropdown : ComboBox
{
    public static readonly AttachedProperty<bool> AllowDismissInteractionProperty = AvaloniaProperty.RegisterAttached<
        Dropdown,
        Control,
        bool
    >("AllowDismissInteraction");

    public static bool GetAllowDismissInteraction(Control control) => control.GetValue(AllowDismissInteractionProperty);

    public static void SetAllowDismissInteraction(Control control, bool value) => control.SetValue(AllowDismissInteractionProperty, value);

    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<Dropdown, string?>(nameof(Note));
    public static readonly StyledProperty<bool> SearchProperty = AvaloniaProperty.Register<Dropdown, bool>(nameof(Search));
    public static readonly StyledProperty<string?> SearchPlaceholderProperty = AvaloniaProperty.Register<Dropdown, string?>(
        nameof(SearchPlaceholder)
    );
    public static readonly StyledProperty<string?> EmptyTextProperty = AvaloniaProperty.Register<Dropdown, string?>(nameof(EmptyText));
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }
    public bool Search
    {
        get => GetValue(SearchProperty);
        set => SetValue(SearchProperty, value);
    }
    public string? SearchPlaceholder
    {
        get => GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }
    public string? EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }
    public static readonly StyledProperty<DropdownVariant> VariantProperty = AvaloniaProperty.Register<Dropdown, DropdownVariant>(
        nameof(Variant)
    );
    public static readonly StyledProperty<string?> ErrorTextProperty = AvaloniaProperty.Register<Dropdown, string?>(nameof(ErrorText));
    public static readonly DirectProperty<Dropdown, bool> HasErrorProperty = AvaloniaProperty.RegisterDirect<Dropdown, bool>(
        nameof(HasError),
        dropdown => dropdown.HasError
    );

    public DropdownVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public string? ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    private bool _hasError;
    public bool HasError => _hasError;

    public Dropdown()
    {
        DropDownOpened += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (Search && IsDropDownOpen)
                        _searchInput?.Focus();
                },
                DispatcherPriority.Input
            );
        ItemsView.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(FilterOptions);
        DataTemplates.Add(
            new FuncDataTemplate<DropdownOption>(
                (option, _) =>
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    if (option.Icon is not null)
                        row.Children.Add(
                            new PathIcon
                            {
                                Classes = { "DropdownOptionIcon" },
                                Data = option.Icon,
                                Width = 16,
                                Height = 16,
                                VerticalAlignment = VerticalAlignment.Center,
                            }
                        );
                    row.Children.Add(new TextBlock { Text = option.Text, VerticalAlignment = VerticalAlignment.Center });
                    return row;
                }
            )
        );
    }

    public static readonly StyledProperty<TimeSpan> MenuAnimationDurationProperty = AvaloniaProperty.Register<Dropdown, TimeSpan>(
        nameof(MenuAnimationDuration)
    );

    public TimeSpan MenuAnimationDuration
    {
        get => GetValue(MenuAnimationDurationProperty);
        set => SetValue(MenuAnimationDurationProperty, value);
    }

    private Popup? _popup;
    private Border? _menu;
    private TopLevel? _owner;
    private CancellationTokenSource? _animation;
    private string _searchTerm = "";
    private long _searchTime;
    private InputField? _searchInput;
    private TextBlock? _empty;

    private static string OptionText(object? item)
    {
        var text = item is AvaloniaObject value ? TextSearch.GetText(value) : null;
        // shortcut: TextBinding is unused in app dropdowns; evaluate it here if that changes.
        return string.IsNullOrEmpty(text) ? (item is ContentControl content ? content.Content : item)?.ToString() ?? "" : text;
    }

    private bool Matches(object? item) =>
        !Search || OptionText(item).Contains(_searchInput?.Text?.Trim() ?? "", StringComparison.CurrentCultureIgnoreCase);

    private void FilterOptions()
    {
        var count = 0;
        for (var i = 0; i < ItemCount; i++)
        {
            var matches = Matches(ItemsView[i]);
            if (matches)
                count++;
            if (ContainerFromIndex(i) is { } container)
                container.IsVisible = matches;
        }
        if (_empty is not null)
            _empty.IsVisible = count == 0;
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e) => FilterOptions();

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        var options = Enumerable
            .Range(0, ItemCount)
            .Where(i =>
                Matches(ItemsView[i])
                && ItemsView[i] is not InputElement { IsEnabled: false }
                && ContainerFromIndex(i) is not { IsEnabled: false }
            )
            .ToArray();
        if (e.Key is Key.Down or Key.Up)
        {
            if (options.Length > 0)
                ContainerFromIndex(options[e.Key == Key.Down ? 0 : options.Length - 1])?.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (options.Length > 0)
            {
                if (!options.Contains(SelectedIndex))
                    SetCurrentValue(SelectedIndexProperty, options[0]);
                SetCurrentValue(IsDropDownOpenProperty, false);
                Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            Focus();
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Handled || !IsTextSearchEnabled || Search && IsDropDownOpen)
            return;
        var now = Environment.TickCount64;
        _searchTerm = now - _searchTime > 1000 ? e.Text ?? "" : _searchTerm + e.Text;
        _searchTime = now;
        for (var index = 0; index < ItemCount; index++)
        {
            var item = ItemsView[index];
            if (item is InputElement { IsEnabled: false } || ContainerFromIndex(index) is { IsEnabled: false })
                continue;
            if (OptionText(item).StartsWith(_searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                SetCurrentValue(SelectedIndexProperty, index);
                break;
            }
        }
        e.Handled = true;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new DropdownItem();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (item is DropdownOption option)
            container.Bind(IsEnabledProperty, new Binding(nameof(DropdownOption.IsEnabled)) { Source = option });
        container.IsVisible = Matches(item);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        if (container is DropdownItem && container.DataContext is DropdownOption)
            container.ClearValue(IsEnabledProperty);
        base.ClearContainerForItemOverride(container);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_searchInput is not null)
        {
            _searchInput.TextChanged -= SearchTextChanged;
            _searchInput.KeyDown -= SearchKeyDown;
        }
        CleanupMenu();
        if (_popup is not null)
            _popup.Closed -= OnPopupClosed;
        base.OnApplyTemplate(e);
        _popup = e.NameScope.Get<Popup>("PART_Popup");
        _menu = e.NameScope.Get<Border>("PART_Menu");
        _searchInput = e.NameScope.Get<InputField>("PART_Search");
        _empty = e.NameScope.Get<TextBlock>("PART_Empty");
        _searchInput.TextChanged += SearchTextChanged;
        _searchInput.KeyDown += SearchKeyDown;
        FilterOptions();
        _popup.Closed += OnPopupClosed;
        if (IsDropDownOpen)
            _ = UpdateMenuAsync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ErrorTextProperty || change.Property == NoteProperty)
        {
            SetAndRaise(HasErrorProperty, ref _hasError, !string.IsNullOrWhiteSpace(ErrorText));
            PseudoClasses.Set(":has-error", HasError);
            PseudoClasses.Set(":has-note", !HasError && !string.IsNullOrWhiteSpace(Note));
        }
        if (change.Property == SearchProperty)
            FilterOptions();
        if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            CleanupMenu();
        }
        if (change.Property == IsDropDownOpenProperty || change.Property == MenuAnimationDurationProperty)
            _ = UpdateMenuAsync();
    }

    public override bool UpdateSelectionFromEvent(Control container, RoutedEventArgs eventArgs)
    {
        // ComboBox normally closes the popup immediately, bypassing the exit animation.
        if (
            !IsDropDownOpen
            || eventArgs.Handled
            || !container.IsEnabled
            || !container.IsVisible
            || !(
                eventArgs switch
                {
                    PointerEventArgs pointer => ShouldTriggerSelection(container, pointer),
                    KeyEventArgs key => ShouldTriggerSelection(container, key),
                    FocusChangedEventArgs => !Search,
                    _ => false,
                }
            )
        )
            return false;
        var index = IndexFromContainer(container);
        if (index < 0)
            return false;
        SetCurrentValue(SelectedIndexProperty, index);
        eventArgs.Handled = true;
        SetCurrentValue(IsDropDownOpenProperty, false);
        return true;
    }

    private async Task UpdateMenuAsync()
    {
        var from = _popup?.IsOpen == true ? _menu?.Opacity ?? 0 : 0;
        _animation?.Cancel();
        _animation?.Dispose();
        _animation = null;
        if (_popup is null || _menu is null)
            return;

        var popup = _popup;
        var menu = _menu;
        var opening = IsDropDownOpen;
        if (!opening && !popup.IsOpen)
            return;

        menu.Opacity = opening ? 1 : 0;
        menu.IsHitTestVisible = opening;
        if (opening)
        {
            _searchInput?.SetCurrentValue(TextBox.TextProperty, "");
            FilterOptions();
            menu.Width = double.NaN;
            menu.Height = double.NaN;
            menu.MinWidth = 0;
            menu.Measure(Size.Infinity);
            menu.MinWidth = Math.Max(Bounds.Width, menu.DesiredSize.Width);
            _owner ??= TopLevel.GetTopLevel(this);
            var center = _owner is null ? null : this.TranslatePoint(new Point(Bounds.Width / 2, 0), _owner);
            popup.Placement =
                center?.X > _owner?.Bounds.Width / 2 ? PlacementMode.BottomEdgeAlignedRight : PlacementMode.BottomEdgeAlignedLeft;
            _owner?.RemoveHandler(PointerPressedEvent, OnOutsidePressed);
            _owner?.AddHandler(PointerPressedEvent, OnOutsidePressed, RoutingStrategies.Tunnel);
            if (_owner is Window window)
            {
                window.Deactivated -= OnOwnerDeactivated;
                window.Deactivated += OnOwnerDeactivated;
            }
            popup.IsOpen = true;
            // The popup must realize its item containers before measuring every option.
            menu.UpdateLayout();
            menu.Measure(Size.Infinity);
            menu.Width = Math.Max(Bounds.Width, menu.DesiredSize.Width);
            if (Search)
                // Keep the native popup from resizing/repositioning on every search keystroke.
                menu.Height = Math.Min(menu.DesiredSize.Height, Math.Max(0, MaxDropDownHeight - menu.Margin.Top - menu.Margin.Bottom));
        }

        if (MenuAnimationDuration > TimeSpan.Zero)
        {
            var cancellation = new CancellationTokenSource();
            _animation = cancellation;
            var animation = new Animation
            {
                Duration = MenuAnimationDuration,
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(OpacityProperty, from), new Setter(ScaleTransform.ScaleYProperty, from) },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(OpacityProperty, opening ? 1d : 0d),
                            new Setter(ScaleTransform.ScaleYProperty, opening ? 1d : 0d),
                        },
                    },
                },
            };
            try
            {
                await animation.RunAsync(menu, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (cancellation.IsCancellationRequested)
                return;
        }
        if (!opening)
            popup.IsOpen = false;
    }

    private void OnOutsidePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_popup is null || e.Source is not Visual source || _popup.IsInsidePopup(source))
            return;
        SetCurrentValue(IsDropDownOpenProperty, false);
        if (source.GetVisualAncestors().Prepend(source).OfType<Control>().Any(GetAllowDismissInteraction))
            CleanupMenu();
        else
            e.Handled = true;
    }

    private void OnOwnerDeactivated(object? sender, EventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        CleanupMenu();
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        CleanupMenu();
        SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void CleanupMenu()
    {
        if (_owner is Window window)
            window.Deactivated -= OnOwnerDeactivated;
        _owner?.RemoveHandler(PointerPressedEvent, OnOutsidePressed);
        _owner = null;
        _animation?.Cancel();
        _animation?.Dispose();
        _animation = null;
        if (_popup is not null)
            _popup.IsOpen = false;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CleanupMenu();
        base.OnDetachedFromVisualTree(e);
    }
}

public class DropdownItem : ComboBoxItem { }

public enum DropdownVariant
{
    Bordered,
    Borderless,
}

// Native ComboBox navigation reads InputElement.IsEnabled before an item container exists.
public sealed class DropdownOption : InputElement
{
    public DropdownOption(string text, Geometry? icon = null, bool isEnabled = true)
    {
        Text = text;
        Icon = icon;
        IsEnabled = isEnabled;
    }

    public string Text { get; }
    public Geometry? Icon { get; }

    public override string ToString() => Text;
}
