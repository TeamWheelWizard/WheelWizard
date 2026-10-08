using System.Diagnostics;
using System.Text.RegularExpressions;
using MiiAnim.Core.Animation;
using Testably.Abstractions.RandomSystem;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiAnimations.Playback;

namespace WheelWizard.MiiAnimations.Editor;

/// <summary>
/// The Mii editor's animation state machine. The Mii idles (picking a new idle every loop), reacts to edits with
/// one-shot clips, and goes back to idling when a reaction ends. Each reaction plays a random clip from the "editor"
/// folder named after it (<see cref="MiiEditorReaction.FavoriteColor"/> → "editor/favorite_color"), so adding a
/// variant is just adding a file to the right folder.
/// <para>
/// States: <b>Idle</b> → (edit) <b>Reacting</b> → (clip done) <b>Idle</b>. Reactions that change what the Mii looks
/// like (gender, randomize) or end the editor (save) are <i>important</i>: edits don't interrupt them.
/// </para>
/// </summary>
public sealed class MiiEditorDirector
{
    private const string Root = "editor";
    private const double IdleFadeSeconds = 0.6;
    private const double ReactionFadeSeconds = 0.2;
    private const double HoldStillFadeSeconds = 0.5;

    /// <summary>The same edit reaction doesn't replay within this long after it ended, so rapid edits don't keep the Mii busy.</summary>
    private static readonly TimeSpan RepeatCooldown = TimeSpan.FromSeconds(1);

    private static readonly HashSet<MiiEditorReaction> Important =
    [
        MiiEditorReaction.Enter,
        MiiEditorReaction.Randomize,
        MiiEditorReaction.Save,
        MiiEditorReaction.BecomeGirl,
        MiiEditorReaction.BecomeBoy,
    ];

    private readonly MiiAnimationPlayer _player;
    private readonly IMiiAnimationLibrary _library;
    private readonly IRandom _random;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, string> _lastPicked = new();
    private readonly List<Waiter> _waiters = [];

    /// <summary>Barely-there breathing for <see cref="HoldStill"/>: the head stays where it is, so parts can be clicked and dragged.</summary>
    private static readonly MiiAnimation StillClip = CreateStillClip();

    private MiiAnimation? _idleClip;
    private bool _holdStill;
    private Reacting? _reacting;
    private MiiEditorReaction? _lastEnded;
    private TimeSpan _lastEndedAt;

    public MiiEditorDirector(MiiAnimationPlayer player, IMiiAnimationLibrary library, IRandom random)
    {
        _player = player;
        _library = library;
        _random = random;
        _player.Finished += OnClipFinished;
        _player.EventReached += OnClipEvent;
    }

    /// <summary>How much the Mii may look at the cursor right now (it's busy during reactions).</summary>
    public float LookWeight =>
        _reacting is not { } reacting ? 1f
        : Important.Contains(reacting.Reaction) ? 0f
        : 0.4f;

    /// <summary>
    /// Keeps the Mii still (just breathing, no idles or edit reactions) while parts of it are being clicked and
    /// dragged. Important reactions (gender swap, save) still play.
    /// </summary>
    public bool HoldStill
    {
        get => _holdStill;
        set
        {
            if (_holdStill == value)
                return;
            _holdStill = value;
            if (_reacting is not { } reacting)
                PlayIdle(HoldStillFadeSeconds);
            else if (value && !Important.Contains(reacting.Reaction))
                EndReaction(playIdle: true, fadeSeconds: HoldStillFadeSeconds);
        }
    }

    /// <summary>Starts idling now, e.g. after a clip played straight on the player (like falling in) ended.</summary>
    public void Idle()
    {
        if (_reacting is not null)
            EndReaction(playIdle: false, fadeSeconds: 0);
        PlayIdle(IdleFadeSeconds);
    }

    /// <summary>Plays the editor's entrance (or just starts idling when there is none).</summary>
    public void Start()
    {
        if (!React(MiiEditorReaction.Enter))
            PlayIdle(0);
    }

    /// <summary>
    /// Plays a reaction to an edit. Returns false when the Mii is busy (an important reaction, or the same one is
    /// already playing or just ended) or there is no clip for it.
    /// </summary>
    public bool React(MiiEditorReaction reaction)
    {
        var important = Important.Contains(reaction);
        if (_holdStill && !important)
            return false;
        if (_reacting is { } playing && !important && (playing.Reaction == reaction || Important.Contains(playing.Reaction)))
            return false;
        if (!important && _lastEnded == reaction && _clock.Elapsed - _lastEndedAt < RepeatCooldown)
            return false;

        if (Pick($"{Root}/{FolderOf(reaction)}") is not { } clip)
            return false;

        if (_reacting is not null)
            EndReaction(playIdle: false, fadeSeconds: 0);
        _reacting = new(reaction, clip);
        _player.Play(clip, loop: false, fadeSeconds: ReactionFadeSeconds);
        return true;
    }

    /// <summary>
    /// Completes when the playing reaction reaches the <paramref name="cue"/> marker, or its end when
    /// <paramref name="cue"/> is null. Also completes when the reaction ends or is replaced before getting there, and
    /// right away when nothing is playing or the cue isn't ahead.
    /// </summary>
    public Task WhenReached(string? cue = null)
    {
        if (_reacting is not { Clip: var clip })
            return Task.CompletedTask;
        if (cue is not null && !(ReferenceEquals(_player.Current, clip) && clip.Events.Any(e => e.Name == cue && e.Frame >= _player.Frame)))
            return Task.CompletedTask;

        var waiter = new Waiter(clip, cue, new(TaskCreationOptions.RunContinuationsAsynchronously));
        _waiters.Add(waiter);
        return waiter.Done.Task;
    }

    private void EndReaction(bool playIdle, double fadeSeconds)
    {
        var ended = _reacting!.Value;
        _reacting = null;
        if (playIdle)
            PlayIdle(fadeSeconds);
        _lastEnded = ended.Reaction;
        _lastEndedAt = _clock.Elapsed;
        Release(waiter => ReferenceEquals(waiter.Clip, ended.Clip));
    }

    private void OnClipFinished(MiiAnimation clip)
    {
        if (ReferenceEquals(clip, _reacting?.Clip))
            EndReaction(playIdle: true, fadeSeconds: IdleFadeSeconds);
        else if (ReferenceEquals(clip, _idleClip))
            PlayIdle(IdleFadeSeconds);
    }

    private void OnClipEvent(MiiAnimation clip, AnimEvent animEvent) =>
        Release(waiter => ReferenceEquals(waiter.Clip, clip) && waiter.Cue == animEvent.Name);

    private void Release(Predicate<Waiter> match)
    {
        foreach (var waiter in _waiters.FindAll(match))
            waiter.Done.TrySetResult();
        _waiters.RemoveAll(match);
    }

    /// <summary>Idles are played once each (they all start and end in the same pose), then another one is picked.</summary>
    private void PlayIdle(double fadeSeconds)
    {
        if (_holdStill)
        {
            _idleClip = StillClip;
            _player.Play(StillClip, loop: true, fadeSeconds);
            return;
        }

        _idleClip = Pick($"{Root}/idle");
        if (_idleClip is not null)
            _player.Play(_idleClip, loop: false, fadeSeconds);
        else
            _player.Stop(fadeSeconds);
    }

    /// <summary>FavoriteColor → "favorite_color".</summary>
    private static string FolderOf(MiiEditorReaction reaction) =>
        Regex.Replace(reaction.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToLowerInvariant();

    /// <summary>A random clip from <paramref name="folder"/>, avoiding the one picked from it last time.</summary>
    private MiiAnimation? Pick(string folder)
    {
        var pool = _library.List(folder);
        _lastPicked.TryGetValue(folder, out var last);
        var choices = pool.Where(path => path != last || pool.Count == 1).ToList();
        while (choices.Count > 0)
        {
            var path = choices[_random.Next(choices.Count)];
            if (_library.Get(path) is { } clip)
            {
                _lastPicked[folder] = path;
                return clip;
            }
            choices.Remove(path);
        }

        return null;
    }

    private static MiiAnimation CreateStillClip()
    {
        var clip = new MiiAnimation
        {
            Name = "Editor_hold_still",
            Fps = 60,
            Length = 240,
        };
        var breathe = clip.GetOrCreateCurve(TrackId.Bone(MiiAnim.Core.Rig.MiiBone.Root, Channel.PosY));
        breathe.SetKey(0, 0f);
        breathe.SetKey(120, -0.35f);
        breathe.SetKey(240, 0f);
        return clip;
    }

    private readonly record struct Reacting(MiiEditorReaction Reaction, MiiAnimation Clip);

    private sealed record Waiter(MiiAnimation Clip, string? Cue, TaskCompletionSource Done);
}
