using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace WheelWizard.Views.Components;

public partial class LapTimesGraph : UserControl, INotifyPropertyChanged
{
    private const double GraphWidth = 400;
    private const double GraphHeight = 120;
    
    private List<int> _lapTimesMs = new();
    private Geometry? _graphPath;
    private Geometry? _graphAreaPath;
    private string _fastestLapText = "--";
    private string _slowestLapText = "--";
    private string _averageLapText = "--";
    private bool _hasData;

    public static readonly StyledProperty<List<int>?> LapTimesProperty = AvaloniaProperty.Register<LapTimesGraph, List<int>?>(
        nameof(LapTimes),
        new List<int>()
    );

    static LapTimesGraph()
    {
        LapTimesProperty.Changed.AddClassHandler<LapTimesGraph>((graph, _) => graph.UpdateGraph());
    }

    public List<int>? LapTimes
    {
        get => GetValue(LapTimesProperty);
        set => SetValue(LapTimesProperty, value);
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

    public string FastestLapText
    {
        get => _fastestLapText;
        set
        {
            _fastestLapText = value;
            OnPropertyChanged(nameof(FastestLapText));
        }
    }

    public string SlowestLapText
    {
        get => _slowestLapText;
        set
        {
            _slowestLapText = value;
            OnPropertyChanged(nameof(SlowestLapText));
        }
    }

    public string AverageLapText
    {
        get => _averageLapText;
        set
        {
            _averageLapText = value;
            OnPropertyChanged(nameof(AverageLapText));
        }
    }

    public bool HasData
    {
        get => _hasData;
        set
        {
            _hasData = value;
            OnPropertyChanged(nameof(HasData));
        }
    }

    public LapTimesGraph()
    {
        InitializeComponent();
    }

    private void UpdateGraph()
    {
        var lapTimes = LapTimes;
        if (lapTimes == null || lapTimes.Count == 0)
        {
            HasData = false;
            ResetGraph();
            return;
        }

        _lapTimesMs = lapTimes.ToList();
        
        if (_lapTimesMs.Count == 0)
        {
            HasData = false;
            ResetGraph();
            return;
        }

        var minTime = _lapTimesMs.Min();
        var maxTime = _lapTimesMs.Max();
        var timeRange = Math.Max(1, maxTime - minTime);
        var averageTime = _lapTimesMs.Average();

        FastestLapText = FormatTime(minTime);
        SlowestLapText = FormatTime(maxTime);
        AverageLapText = FormatTime((int)averageTime);

        var points = new List<Point>();
        for (int i = 0; i < _lapTimesMs.Count; i++)
        {
            var x = (double)i / Math.Max(1, _lapTimesMs.Count - 1) * GraphWidth;
            var y = GraphHeight - ((_lapTimesMs[i] - minTime) / (double)timeRange * GraphHeight);
            points.Add(new Point(x, y));
        }

        if (_lapTimesMs.Count == 1)
        {
            points[0] = new Point(GraphWidth / 2, GraphHeight / 2);
        }

        GraphPath = BuildLineGeometry(points);
        GraphAreaPath = BuildAreaGeometry(points);
        HasData = points.Count > 0;
        
        Dispatcher.UIThread.Post(() => UpdateDataPoints(points), DispatcherPriority.Loaded);
    }

    private void UpdateDataPoints(IReadOnlyList<Point> points)
    {
        if (this.FindControl<Canvas>("DataPointsCanvas") is not Canvas canvas)
            return;
            
        canvas.Children.Clear();
        
        var fastestLapIndex = _lapTimesMs.IndexOf(_lapTimesMs.Min());
        
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var circle = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = i == fastestLapIndex ? App.Current?.Resources["Primary"] as IBrush : App.Current?.Resources["Neutral300"] as IBrush,
                Stroke = App.Current?.Resources["Neutral950"] as IBrush,
                StrokeThickness = 1
            };
            
            Canvas.SetLeft(circle, point.X - 3);
            Canvas.SetTop(circle, point.Y - 3);
            canvas.Children.Add(circle);
        }
    }

    private void ResetGraph()
    {
        GraphPath = null;
        GraphAreaPath = null;
        FastestLapText = "--";
        SlowestLapText = "--";
        AverageLapText = "--";
    }

    private static string FormatTime(int milliseconds)
    {
        var totalSeconds = milliseconds / 1000.0;
        var minutes = (int)totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0 ? $"{minutes}:{seconds:00.000}" : $"{seconds:0.000}";
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
            StartPoint = new Point(points[0].X, GraphHeight),
            IsFilled = true,
            IsClosed = true,
            Segments = [],
        };

        foreach (var point in points)
            figure.Segments.Add(new LineSegment { Point = point });

        figure.Segments.Add(new LineSegment { Point = new Point(points[^1].X, GraphHeight) });

        var geometry = new PathGeometry { Figures = [] };
        geometry.Figures.Add(figure);
        return geometry;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}