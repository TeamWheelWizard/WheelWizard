using System.Diagnostics;
using Avalonia.Threading;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups.Generic;

public partial class ProgressWindow : PopupContent
{
    private Stopwatch _stopwatch = new();
    private int _progress = 0;
    private double? _totalMb = null;
    private DispatcherTimer _updateTimer;

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
        _stopwatch.Start();
        _updateTimer.Start();
    }

    protected override void BeforeClose()
    {
        _stopwatch.Stop();
        _updateTimer.Stop();
    }

    private void UpdateTimer_Tick(object sender, EventArgs e)
    {
        InternalUpdate();
    }

    private void InternalUpdate()
    {
        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        var remainingSeconds = (100 - _progress) / (_progress / elapsedSeconds);

        var remainingText = _progress <= 0 ? Common.State_Unknown : Humanizer.HumanizeSeconds((int)remainingSeconds);

        var bottomText = $"{Phrases.Progress_EstimatedTimeRemaining} {remainingText}";

        if (_totalMb != null)
        {
            var downloadedMb = (_progress / 100.0) * (double)_totalMb;
            bottomText = $"{Common.Attribute_Speed}: {downloadedMb / elapsedSeconds:F2} MB/s | {bottomText}";
        }

        LiveTextBlock.Text = bottomText;
        ProgressBar.Value = _progress;
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
        GoalTextBlock.Text = Humanizer.ReplaceDynamic(Phrases.Progress_DownloadingMb, $"{megaBytes:F2}");
        return this;
    }

    public void UpdateProgress(int progress)
    {
        _progress = progress;
        // No need to call InternalUpdate directly, it's handled by the timer
    }
}
