using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.RrRooms;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.Services;
using WheelWizard.Views;

namespace WheelWizard.Views.Components;

public partial class VrHistoryGraph : UserControlBase, INotifyPropertyChanged
{
    private const int DefaultHistoryDays = 30;
    private const double GraphWidth = 1000;
    private const double GraphHeight = 260;

    [Inject]
    private IApiCaller<IRwfcApi> ApiCaller { get; set; } = null!;

    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private string _emptyStateText = "No VR history found for this range yet.";
    private int _startingVr;
    private int _endingVr;
    private int _totalChange;
    private int _biggestGain;
    private int _biggestDrop;
    private int _entriesCount;
    private string _dateRangeText = string.Empty;
    private bool _hasData;
    private int _requestVersion;
    private int _reloadVersion;
    private bool _isInitializingDaysDropdown;
    private int _selectedHistoryDays = DefaultHistoryDays;
    private Geometry? _graphPath;
    private Geometry? _graphAreaPath;
    private string _graphStartLabel = string.Empty;
    private string _graphMidLabel = string.Empty;
    private string _graphEndLabel = string.Empty;
    private string _graphMaxVrText = "--";
    private string _graphMinVrText = "--";

    public static readonly StyledProperty<string?> FriendCodeProperty = AvaloniaProperty.Register<VrHistoryGraph, string?>(
        nameof(FriendCode),
        string.Empty
    );

    static VrHistoryGraph()
    {
        FriendCodeProperty.Changed.AddClassHandler<VrHistoryGraph>((graph, _) => graph.TriggerHistoryReload());
    }

    public string? FriendCode
    {
        get => GetValue(FriendCodeProperty);
        set => SetValue(FriendCodeProperty, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(ShowNoDataState));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ShowNoDataState));
        }
    }

    public string EmptyStateText
    {
        get => _emptyStateText;
        set
        {
            _emptyStateText = value;
            OnPropertyChanged(nameof(EmptyStateText));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public int StartingVr
    {
        get => _startingVr;
        set
        {
            _startingVr = value;
            OnPropertyChanged(nameof(StartingVr));
        }
    }

    public int EndingVr
    {
        get => _endingVr;
        set
        {
            _endingVr = value;
            OnPropertyChanged(nameof(EndingVr));
        }
    }

    public int TotalChange
    {
        get => _totalChange;
        set
        {
            _totalChange = value;
            OnPropertyChanged(nameof(TotalChange));
            OnPropertyChanged(nameof(TotalChangeText));
            OnPropertyChanged(nameof(IsTotalChangeGain));
            OnPropertyChanged(nameof(IsTotalChangeLoss));
            OnPropertyChanged(nameof(IsTotalChangeNeutral));
        }
    }

    public int BiggestGain
    {
        get => _biggestGain;
        set
        {
            _biggestGain = value;
            OnPropertyChanged(nameof(BiggestGain));
            OnPropertyChanged(nameof(BiggestGainText));
        }
    }

    public int BiggestDrop
    {
        get => _biggestDrop;
        set
        {
            _biggestDrop = value;
            OnPropertyChanged(nameof(BiggestDrop));
            OnPropertyChanged(nameof(BiggestDropText));
        }
    }

    public int EntriesCount
    {
        get => _entriesCount;
        set
        {
            _entriesCount = value;
            OnPropertyChanged(nameof(EntriesCount));
        }
    }

    public string DateRangeText
    {
        get => _dateRangeText;
        set
        {
            _dateRangeText = value;
            OnPropertyChanged(nameof(DateRangeText));
        }
    }

    public bool HasData
    {
        get => _hasData;
        set
        {
            _hasData = value;
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(ShowNoDataState));
        }
    }

    public Geometry? GraphPath
    {
        get => _graphPath;
        set
        {
            _graphPath = value;
            OnPropertyChanged(nameof(GraphPath));
        }
    }

    public Geometry? GraphAreaPath
    {
        get => _graphAreaPath;
        set
        {
            _graphAreaPath = value;
            OnPropertyChanged(nameof(GraphAreaPath));
        }
    }

    public string GraphStartLabel
    {
        get => _graphStartLabel;
        set
        {
            _graphStartLabel = value;
            OnPropertyChanged(nameof(GraphStartLabel));
        }
    }

    public string GraphMidLabel
    {
        get => _graphMidLabel;
        set
        {
            _graphMidLabel = value;
            OnPropertyChanged(nameof(GraphMidLabel));
        }
    }

    public string GraphEndLabel
    {
        get => _graphEndLabel;
        set
        {
            _graphEndLabel = value;
            OnPropertyChanged(nameof(GraphEndLabel));
        }
    }

    public string GraphMaxVrText
    {
        get => _graphMaxVrText;
        set
        {
            _graphMaxVrText = value;
            OnPropertyChanged(nameof(GraphMaxVrText));
        }
    }

    public string GraphMinVrText
    {
        get => _graphMinVrText;
        set
        {
            _graphMinVrText = value;
            OnPropertyChanged(nameof(GraphMinVrText));
        }
    }

    public bool ShowNoDataState => !IsLoading && !HasError && !HasData;

    public string TotalChangeText => FormatSignedValue(TotalChange);

    public string BiggestGainText => FormatSignedValue(BiggestGain);

    public string BiggestDropText => FormatSignedValue(BiggestDrop);

    public bool IsTotalChangeGain => TotalChange > 0;

    public bool IsTotalChangeLoss => TotalChange < 0;

    public bool IsTotalChangeNeutral => TotalChange == 0;

    public VrHistoryGraph()
    {
        InitializeComponent();
        PopulateHistoryDaysDropdown();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TriggerHistoryReload();
    }

    private void PopulateHistoryDaysDropdown()
    {
        _isInitializingDaysDropdown = true;
        HistoryDaysDropdown.Items.Clear();
        HistoryDaysDropdown.Items.Add(new HistoryDaysOption(1, "Last 24 hours"));
        HistoryDaysDropdown.Items.Add(new HistoryDaysOption(7, "Last 7 days"));
        HistoryDaysDropdown.Items.Add(new HistoryDaysOption(30, "Last 30 days"));
        HistoryDaysDropdown.Items.Add(new HistoryDaysOption(60, "Last 60 days"));

        foreach (var item in HistoryDaysDropdown.Items.OfType<HistoryDaysOption>())
        {
            if (item.Days != DefaultHistoryDays)
                continue;

            HistoryDaysDropdown.SelectedItem = item;
            break;
        }

        _isInitializingDaysDropdown = false;
    }

    private void TriggerHistoryReload()
    {
        if (!this.IsAttachedToVisualTree())
            return;

        var reloadVersion = Interlocked.Increment(ref _reloadVersion);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (reloadVersion != _reloadVersion || !this.IsAttachedToVisualTree())
                    return;

                _ = ReloadHistoryAsync();
            },
            DispatcherPriority.Background
        );
    }

    private async Task ReloadHistoryAsync()
    {
        var friendCode = NormalizeFriendCode(FriendCode);
        if (IsMissingFriendCode(friendCode))
        {
            SetNoFriendCodeState();
            return;
        }

        var requestVersion = Interlocked.Increment(ref _requestVersion);

        IsLoading = true;
        ErrorMessage = string.Empty;

        var result = await ApiCaller.CallApiAsync(api => api.GetPlayerVrHistoryAsync(friendCode, _selectedHistoryDays));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (requestVersion != _requestVersion)
                return;

            if (result.IsSuccess && result.Value != null)
            {
                ApplyHistoryData(result.Value, _selectedHistoryDays);
            }
            else
            {
                HasData = false;
                ResetGraph();
                ErrorMessage = result.Error?.Message ?? "Failed to load VR history";
            }

            IsLoading = false;
        });
    }

    private void SetNoFriendCodeState()
    {
        IsLoading = false;
        ErrorMessage = string.Empty;
        HasData = false;
        EmptyStateText = "Friend code is required to load VR history.";
        DateRangeText = string.Empty;
        StartingVr = 0;
        EndingVr = 0;
        TotalChange = 0;
        BiggestGain = 0;
        BiggestDrop = 0;
        EntriesCount = 0;
        ResetGraph();
    }

    private void ApplyHistoryData(RwfcPlayerVrHistoryResponse historyResponse, int days)
    {
        EmptyStateText = "No VR history found for this range yet.";
        StartingVr = historyResponse.StartingVr;
        EndingVr = historyResponse.EndingVr;
        TotalChange = historyResponse.TotalVrChange;

        var orderedByDate = historyResponse.History.OrderBy(entry => entry.Date).ToList();
        EntriesCount = orderedByDate.Count;

        var gains = orderedByDate.Where(entry => entry.VrChange > 0).Select(entry => entry.VrChange).DefaultIfEmpty(0);
        var losses = orderedByDate.Where(entry => entry.VrChange < 0).Select(entry => entry.VrChange).DefaultIfEmpty(0);
        BiggestGain = gains.Max();
        BiggestDrop = losses.Min();

        var fromDate = historyResponse.FromDate.ToLocalTime().ToString("MMM d");
        var toDate = historyResponse.ToDate.ToLocalTime().ToString("MMM d");
        DateRangeText = $"Past {days} day(s) • {fromDate} to {toDate}";

        if (orderedByDate.Count == 0)
        {
            HasData = false;
            ResetGraph();
            return;
        }

        var graphStart = orderedByDate[0].Date;
        var graphEnd = orderedByDate[^1].Date;
        var totalSeconds = Math.Max(1, (graphEnd - graphStart).TotalSeconds);

        var minVr = orderedByDate.Min(entry => entry.TotalVr);
        var maxVr = orderedByDate.Max(entry => entry.TotalVr);
        var vrRange = Math.Max(1, maxVr - minVr);

        GraphMinVrText = minVr.ToString("N0");
        GraphMaxVrText = maxVr.ToString("N0");
        var labelFormat = days >= 7 ? "MMM d" : "MMM d HH:mm";
        GraphStartLabel = graphStart.ToLocalTime().ToString(labelFormat);
        GraphMidLabel = graphStart.AddSeconds(totalSeconds / 2).ToLocalTime().ToString(labelFormat);
        GraphEndLabel = graphEnd.ToLocalTime().ToString(labelFormat);

        var points = orderedByDate
            .Select(entry =>
            {
                var seconds = (entry.Date - graphStart).TotalSeconds;
                var x = seconds / totalSeconds * GraphWidth;
                var y = GraphHeight - (entry.TotalVr - minVr) / (double)vrRange * GraphHeight;
                return new Point(x, y);
            })
            .ToList();

        GraphPath = BuildLineGeometry(points);
        GraphAreaPath = BuildAreaGeometry(points);
        HasData = points.Count > 0;
    }

    private void ResetGraph()
    {
        GraphPath = null;
        GraphAreaPath = null;
        GraphStartLabel = string.Empty;
        GraphMidLabel = string.Empty;
        GraphEndLabel = string.Empty;
        GraphMaxVrText = "--";
        GraphMinVrText = "--";
    }

    private static Geometry? BuildLineGeometry(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            return null;

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsFilled = false,
            IsClosed = false,
            Segments = [],
        };

        for (var i = 1; i < points.Count; i++)
            figure.Segments.Add(new LineSegment { Point = points[i] });

        var geometry = new PathGeometry { Figures = [] };
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Geometry? BuildAreaGeometry(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            return null;

        var figure = new PathFigure
        {
            StartPoint = new Point(0, GraphHeight),
            IsFilled = true,
            IsClosed = true,
            Segments = [],
        };

        foreach (var point in points)
            figure.Segments.Add(new LineSegment { Point = point });

        figure.Segments.Add(new LineSegment { Point = new Point(GraphWidth, GraphHeight) });

        var geometry = new PathGeometry { Figures = [] };
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void HistoryDaysDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingDaysDropdown)
            return;

        if (HistoryDaysDropdown.SelectedItem is not HistoryDaysOption option)
            return;

        if (_selectedHistoryDays == option.Days)
            return;

        _selectedHistoryDays = option.Days;
        TriggerHistoryReload();
    }

    private static string FormatSignedValue(int value)
    {
        if (value > 0)
            return $"+{value:N0}";

        return value.ToString("N0");
    }

    private static string NormalizeFriendCode(string? friendCode)
    {
        var trimmed = friendCode?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 12)
            return $"{digits[..4]}-{digits.Substring(4, 4)}-{digits.Substring(8, 4)}";

        return trimmed;
    }

    private static bool IsMissingFriendCode(string friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return true;

        var digits = new string(friendCode.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return true;

        return digits.All(digit => digit == '0');
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class HistoryDaysOption(int days, string label)
{
    public int Days { get; } = days;
    public string Label { get; } = label;

    public override string ToString() => Label;
}
