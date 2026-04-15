using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace WheelWizard.Views.Patterns;

public class TrackListItem : TemplatedControl
{
    public static readonly StyledProperty<string> TrackNameProperty =
        AvaloniaProperty.Register<TrackListItem, string>(nameof(TrackName), string.Empty);

    public static readonly StyledProperty<string> ConsoleProperty =
        AvaloniaProperty.Register<TrackListItem, string>(nameof(Console), string.Empty);

    public static readonly StyledProperty<string> ConsoleColorProperty =
        AvaloniaProperty.Register<TrackListItem, string>(nameof(ConsoleColor), string.Empty);

    public static readonly StyledProperty<bool> IsCustomTrackProperty =
        AvaloniaProperty.Register<TrackListItem, bool>(nameof(IsCustomTrack), false);

    public static readonly StyledProperty<bool> IsRetroTrackProperty =
        AvaloniaProperty.Register<TrackListItem, bool>(nameof(IsRetroTrack), false);

    public static readonly StyledProperty<string> HexValueProperty =
        AvaloniaProperty.Register<TrackListItem, string>(nameof(HexValue), string.Empty);

    public string TrackName
    {
        get => GetValue(TrackNameProperty);
        set => SetValue(TrackNameProperty, value);
    }

    public string Console
    {
        get => GetValue(ConsoleProperty);
        set => SetValue(ConsoleProperty, value);
    }

    public string ConsoleColor
    {
        get => GetValue(ConsoleColorProperty);
        set => SetValue(ConsoleColorProperty, value);
    }

    public bool IsCustomTrack
    {
        get => GetValue(IsCustomTrackProperty);
        set => SetValue(IsCustomTrackProperty, value);
    }

    public bool IsRetroTrack
    {
        get => GetValue(IsRetroTrackProperty);
        set => SetValue(IsRetroTrackProperty, value);
    }

    public string HexValue
    {
        get => GetValue(HexValueProperty);
        set => SetValue(HexValueProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? ViewTimesClick;

    private TextBlock? _consoleLabel;
    private Border? _customBadge;
    private Border? _retroBadge;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var viewTimesButton = e.NameScope.Find<Button>("PART_ViewTimesButton");
        if (viewTimesButton != null)
        {
            viewTimesButton.Click += (_, args) => 
            {
                ViewTimesClick?.Invoke(this, args);
            };
        }

        _consoleLabel = e.NameScope.Find<TextBlock>("PART_ConsoleLabel");
        _customBadge = e.NameScope.Find<Border>("PART_CustomBadge");
        _retroBadge = e.NameScope.Find<Border>("PART_RetroBadge");

        UpdateVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCustomTrackProperty || change.Property == IsRetroTrackProperty)
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility()
    {
        if (_consoleLabel != null)
        {
            _consoleLabel.IsVisible = false;
        }

        if (_customBadge != null)
        {
            _customBadge.IsVisible = IsCustomTrack;
        }

        if (_retroBadge != null)
        {
            _retroBadge.IsVisible = IsRetroTrack;
        }
    }
}