using System.Diagnostics;
using MiiAnim.Core.Animation;
using Testably.Abstractions.RandomSystem;
using WheelWizard.MiiAnimations.Library;

namespace WheelWizard.MiiAnimations.Playback;

/// <summary>
/// What a Mii does in one spot of the app while nothing else is going on: it idles with random clips from
/// <see cref="Idles"/> and now and then plays an extra from <see cref="Extras"/> (a wave, say). These are library
/// folders, so adding a variant is just adding a file. All clips of a performance start and end in the same pose.
/// </summary>
public sealed record MiiPerformance(params string[] Idles)
{
    /// <summary>Played now and then instead of the next idle.</summary>
    public string? Extras { get; init; }

    /// <summary>Shortest time between extras.</summary>
    public TimeSpan ExtrasAfter { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Longest time between extras.</summary>
    public TimeSpan ExtrasBefore { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Played first when a Mii that's already showing switches to this performance (e.g. waking up).</summary>
    public string? Arrival { get; init; }
}

/// <summary>
/// Runs a <see cref="MiiPerformance"/> on a <see cref="MiiAnimationPlayer"/>: idles one after another (each played
/// once, then the next is picked), extras now and then, and one-off clips like entrances and reactions, after which
/// it goes back to idling.
/// </summary>
public sealed class MiiPerformer
{
    private const double IdleFadeSeconds = 0.5;
    private const double ClipFadeSeconds = 0.2;

    /// <summary>A desynced idle starts no further in than this, so it still plays for a while before the next one.</summary>
    private const double MaxDesync = 0.85;

    private readonly MiiAnimationPlayer _player;
    private readonly MiiClipPicker _picker;
    private readonly IRandom _random;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private MiiAnimation? _playing;
    private Action? _then;
    private TimeSpan _nextExtra;

    public MiiPerformer(MiiAnimationPlayer player, IMiiAnimationLibrary library, IRandom random)
    {
        _player = player;
        _picker = new MiiClipPicker(library, random);
        _random = random;
        _player.Finished += OnFinished;
    }

    public MiiPerformance? Performance { get; private set; }

    /// <summary>
    /// Switches to <paramref name="performance"/>. With <paramref name="arrive"/> its <see cref="MiiPerformance.Arrival"/>
    /// plays first. With <paramref name="desync"/> idling starts part way into a random idle, so a list of Miis doesn't
    /// move in step.
    /// </summary>
    public void Perform(MiiPerformance performance, bool arrive = false, bool desync = false)
    {
        Performance = performance;
        ScheduleExtra();
        if (arrive && performance.Arrival is { } arrival && Play([arrival]))
            return;
        Idle(arrive ? IdleFadeSeconds : 0, desync);
    }

    /// <summary>Switches to <paramref name="performance"/>, starting with <paramref name="entrance"/> (no cross-fade).</summary>
    public void Perform(MiiPerformance performance, MiiAnimation entrance)
    {
        Performance = performance;
        ScheduleExtra();
        Play(entrance, fadeSeconds: 0);
    }

    /// <summary>
    /// Plays a random clip from <paramref name="folders"/> now, then goes back to the performance. False when there
    /// is no clip.
    /// </summary>
    public bool Play(IReadOnlyList<string> folders, double fadeSeconds = ClipFadeSeconds)
    {
        if (_picker.Pick(folders) is not { } clip)
            return false;
        Play(clip, fadeSeconds);
        return true;
    }

    /// <summary>
    /// Plays <paramref name="clip"/> now. When it ends, <paramref name="then"/> runs instead of going back to the
    /// performance (the Mii holds the clip's last pose until something else plays).
    /// </summary>
    public void Play(MiiAnimation clip, double fadeSeconds = ClipFadeSeconds, Action? then = null)
    {
        _playing = clip;
        _then = then;
        _player.Play(clip, loop: false, fadeSeconds);
    }

    /// <summary>A random clip from <paramref name="folders"/> without playing it.</summary>
    public MiiAnimation? Pick(IReadOnlyList<string> folders) => _picker.Pick(folders);

    /// <summary>Stops listening to the player.</summary>
    public void Detach() => _player.Finished -= OnFinished;

    private void OnFinished(MiiAnimation clip)
    {
        if (!ReferenceEquals(clip, _playing))
            return;
        if (_then is { } then)
        {
            _then = null;
            then();
            return;
        }

        if (Performance?.Extras is { } extras && _clock.Elapsed >= _nextExtra)
        {
            ScheduleExtra();
            if (Play([extras], IdleFadeSeconds))
                return;
        }
        Idle(IdleFadeSeconds, desync: false);
    }

    private void Idle(double fadeSeconds, bool desync)
    {
        _then = null;
        if (Performance is not { } performance || _picker.Pick(performance.Idles) is not { } idle)
        {
            _playing = null;
            _player.Stop(fadeSeconds);
            return;
        }

        _playing = idle;
        _player.Play(idle, loop: false, fadeSeconds);
        if (desync)
            _player.Seek(_random.NextDouble() * idle.Length * MaxDesync);
    }

    private void ScheduleExtra()
    {
        if (Performance is not { } performance)
            return;
        var spread = performance.ExtrasBefore - performance.ExtrasAfter;
        _nextExtra = _clock.Elapsed + performance.ExtrasAfter + spread * _random.NextDouble();
    }
}
