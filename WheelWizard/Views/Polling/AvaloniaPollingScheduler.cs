using Avalonia.Threading;
using WheelWizard.Shared.Polling;

namespace WheelWizard.Views.Polling;

public sealed class AvaloniaPollingScheduler : IPollingScheduler
{
    public IDisposable Schedule(TimeSpan interval, Func<Task> callback) => new ScheduledCallback(interval, callback);

    private sealed class ScheduledCallback : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Func<Task> _callback;

        public ScheduledCallback(TimeSpan interval, Func<Task> callback)
        {
            _callback = callback;
            _timer = new() { Interval = interval };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private async void OnTick(object? sender, EventArgs args) => await _callback();

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
