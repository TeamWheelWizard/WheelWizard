using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups.Generic;

public partial class ProgressWindow : PopupContent
{
    private const double EtaWarmupSeconds = 5;
    private const double EtaIncreaseSmoothingSeconds = 5;
    private const double EtaDecreaseSmoothingSeconds = 30;
    private const double InitialSecondsPerRemainingPercent = 2;

    private readonly Stopwatch _stopwatch = new();
    private readonly object _progressLock = new();
    private int _progress = 0;
    private double? _totalMb = null;
    private readonly DispatcherTimer _updateTimer;
    private CancellationTokenSource? _downloadCancellationTokenSource;
    private double? _smoothedRemainingSeconds;
    private double _lastEstimateUpdateSeconds;

    public bool WasCancellationRequested { get; private set; }

    public ProgressWindow()
        : this("Progress Window") { }

    public ProgressWindow(string windowTitle)
        : base(false, false, true, windowTitle)
    {
        InitializeComponent();
        _updateTimer = new();
        _updateTimer.Interval = TimeSpan.FromMilliseconds(100); // Update every 100ms
        _updateTimer.Tick += UpdateTimer_Tick;
    }

    protected override void BeforeOpen()
    {
        lock (_progressLock)
        {
            _smoothedRemainingSeconds = null;
            _lastEstimateUpdateSeconds = 0;
        }

        _stopwatch.Restart();
        _updateTimer.Start();
    }

    protected override void BeforeClose()
    {
        _stopwatch.Stop();
        _updateTimer.Stop();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        InternalUpdate();
    }

    private void InternalUpdate()
    {
        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        int progress;
        double? remainingSeconds;
        lock (_progressLock)
        {
            remainingSeconds = EstimateRemainingSeconds(elapsedSeconds);
            progress = _progress;
        }

        var remainingText = remainingSeconds is null ? t("state.unknown") : tTime((int)Math.Ceiling(remainingSeconds.Value));

        var bottomText = $"{t("progress.estimated_time_remaining")} {remainingText}";

        if (_totalMb != null && elapsedSeconds > 0)
        {
            var downloadedMb = (progress / 100.0) * (double)_totalMb;
            bottomText = $"{t("attribute.speed")}: {downloadedMb / elapsedSeconds:F2} MB/s | {bottomText}";
        }

        LiveTextBlock.Text = bottomText;
        ProgressBar.Value = progress;
    }

    public ProgressWindow SetExtraText(string mainText)
    {
        ExtraTextBlock.Text = mainText;
        return this;
    }

    public ProgressWindow SetGoal(string extraText, double? megaBytes = null)
    {
        _totalMb = megaBytes;
        GoalTextBlock.Text = megaBytes == null ? extraText : $"{extraText} ({megaBytes:F2} MB)";
        return this;
    }

    public ProgressWindow SetGoal(double megaBytes)
    {
        _totalMb = megaBytes;
        GoalTextBlock.Text = t("progress.downloading_mb", $"{megaBytes:F2}");
        return this;
    }

    public ProgressWindow SetIndeterminate(bool isIndeterminate = true)
    {
        ProgressBar.IsIndeterminate = isIndeterminate;
        LiveTextBlock.IsVisible = !isIndeterminate;
        return this;
    }

    public void UpdateProgress(int progress)
    {
        var clampedProgress = Math.Clamp(progress, 0, 100);
        lock (_progressLock)
            _progress = clampedProgress;
        // No need to call InternalUpdate directly, it's handled by the timer
    }

    private double? EstimateRemainingSeconds(double elapsedSeconds)
    {
        if (_progress >= 100)
            return 0;
        if (elapsedSeconds < EtaWarmupSeconds || _progress <= 0)
            return null;

        // Whole-operation progress remains useful when a backend reports only a few, unevenly
        // spaced updates. The small uncertainty floor prevents an early jump to a few seconds when
        // a setup phase begins at an already-weighted percentage.
        var remainingProgress = 100 - _progress;
        var averageRemainingSeconds = elapsedSeconds * remainingProgress / _progress;
        var rawRemainingSeconds = Math.Max(averageRemainingSeconds, remainingProgress * InitialSecondsPerRemainingPercent);
        if (!double.IsFinite(rawRemainingSeconds) || rawRemainingSeconds < 0)
            return null;

        if (_smoothedRemainingSeconds is null)
        {
            _smoothedRemainingSeconds = rawRemainingSeconds;
        }
        else
        {
            var updateSeconds = Math.Max(0, elapsedSeconds - _lastEstimateUpdateSeconds);
            var smoothingSeconds =
                rawRemainingSeconds > _smoothedRemainingSeconds ? EtaIncreaseSmoothingSeconds : EtaDecreaseSmoothingSeconds;
            var weight = 1 - Math.Exp(-updateSeconds / smoothingSeconds);
            _smoothedRemainingSeconds += weight * (rawRemainingSeconds - _smoothedRemainingSeconds.Value);
        }

        _lastEstimateUpdateSeconds = elapsedSeconds;
        return _smoothedRemainingSeconds;
    }

    public ProgressWindow SetCancellationTokenSource(CancellationTokenSource? cancellationTokenSource)
    {
        _downloadCancellationTokenSource = cancellationTokenSource;
        if (cancellationTokenSource != null)
            WasCancellationRequested = false;

        CancelButton.IsVisible = cancellationTokenSource != null;
        CancelButton.IsEnabled = cancellationTokenSource is { IsCancellationRequested: false };
        return this;
    }

    public ProgressWindow MarkCancellationRequested()
    {
        WasCancellationRequested = true;
        CancelButton.IsEnabled = false;
        return SetExtraText($"{t("action.cancel")}...");
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadCancellationTokenSource == null || _downloadCancellationTokenSource.IsCancellationRequested)
            return;

        _downloadCancellationTokenSource.Cancel();
        MarkCancellationRequested();
    }
}
