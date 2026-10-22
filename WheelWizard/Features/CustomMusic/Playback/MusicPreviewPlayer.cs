using System.Diagnostics;

namespace WheelWizard.CustomMusic.Playback;

public enum PreviewState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>Plays a <see cref="PreviewClip"/> with pause and seek on top of a simple "play this file" output.</summary>
public interface IMusicPreviewPlayer : IDisposable
{
    bool IsAvailable { get; }
    PreviewState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>Raised when the state changes and about every 100 ms while playing. May be raised from any thread.</summary>
    event EventHandler? Changed;

    void Play(PreviewClip clip);
    void Pause();
    void Resume();
    void Stop();
    void Seek(TimeSpan position);
}

public sealed class MusicPreviewPlayer(IAudioOutput output) : IMusicPreviewPlayer
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();
    private Timer? _timer;
    private PreviewClip? _clip;
    private TimeSpan _offset;
    private PreviewState _state;

    public bool IsAvailable => output.IsAvailable;

    public PreviewState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
                return _clip?.Duration ?? TimeSpan.Zero;
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_gate)
                return CurrentPosition();
        }
    }

    public event EventHandler? Changed;

    public void Play(PreviewClip clip)
    {
        lock (_gate)
        {
            _clip = clip;
            StartAt(TimeSpan.Zero);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_state != PreviewState.Playing)
                return;
            _offset = CurrentPosition();
            _clock.Reset();
            output.Stop();
            StopTimer();
            _state = PreviewState.Paused;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_state != PreviewState.Paused || _clip == null)
                return;
            StartAt(_offset);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_state == PreviewState.Stopped)
                return;
            StopLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_clip == null || _state == PreviewState.Stopped)
                return;
            var target = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, _clip.Duration.TotalSeconds - 0.25)));
            if (_state == PreviewState.Playing)
                StartAt(target);
            else
                _offset = target;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan CurrentPosition()
    {
        var position = _state == PreviewState.Playing ? _offset + _clock.Elapsed : _offset;
        var duration = _clip?.Duration ?? TimeSpan.Zero;
        return position > duration ? duration : position;
    }

    private void StartAt(TimeSpan start)
    {
        try
        {
            output.Start(_clip!.ToWav(start));
        }
        catch
        {
            StopLocked();
            throw;
        }

        _offset = start;
        _clock.Restart();
        _state = PreviewState.Playing;
        _timer ??= new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        _timer.Change(100, 100);
    }

    private void StopLocked()
    {
        output.Stop();
        StopTimer();
        _clock.Reset();
        _offset = TimeSpan.Zero;
        _state = PreviewState.Stopped;
    }

    private void StopTimer() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    private void Tick()
    {
        lock (_gate)
        {
            if (_state != PreviewState.Playing)
                return;
            if (CurrentPosition() >= (_clip?.Duration ?? TimeSpan.Zero))
                StopLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopLocked();
            _timer?.Dispose();
            _timer = null;
        }

        output.Dispose();
    }
}
