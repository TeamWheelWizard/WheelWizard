using System.Diagnostics;
using MiiAnim.Core.Animation;
using Testably.Abstractions.RandomSystem;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiAnimations.Playback;

namespace WheelWizard.MiiAnimations.Editor;

/// <summary>
/// The Mii editor's animation state machine. The Mii idles (picking a new idle every loop), reacts to edits with
/// one-shot clips, and goes back to idling when a reaction ends. Clips come from the "editor" folder
/// of <see cref="IMiiAnimationLibrary"/>, so adding a variant is just adding a file to the right folder.
/// <para>
/// States: <b>Idle</b> → (edit) <b>Reacting</b> → (clip done) <b>Idle</b>. Reactions that change what the Mii looks
/// like (gender, randomize) or end the editor (save) are <i>important</i>: edits don't interrupt them.
/// </para>
/// </summary>
public sealed class MiiEditorDirector
{
    private const string Root = "editor";
    private const string CloseUpSuffix = "_upper";
    private const double IdleFadeSeconds = 0.6;
    private const double ReactionFadeSeconds = 0.2;
    private const double FocusFadeSeconds = 0.5;

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

    /// <summary>Barely-there breathing for <see cref="HoldStill"/>: the head stays where it is, so parts can be clicked and dragged.</summary>
    private static readonly MiiAnimation StillClip = CreateStillClip();

    private MiiAnimation? _idleClip;
    private bool _holdStill;
    private MiiAnimation? _reactionClip;
    private string? _reactionPath;
    private MiiEditorReaction? _reaction;
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

    /// <summary>An event marker of the playing reaction, e.g. <see cref="MiiEditorCues.SwapGender"/>.</summary>
    public event Action<MiiEditorReaction, string>? Cue;

    /// <summary>A reaction finished or was replaced. Anything still waiting for one of its cues should happen now.</summary>
    public event Action<MiiEditorReaction>? ReactionEnded;

    public MiiEditorFocus Focus { get; private set; } = MiiEditorFocus.Body;

    /// <summary>The reaction playing, or null while idling.</summary>
    public MiiEditorReaction? Reaction => _reaction;

    public bool IsReacting => _reactionClip is not null;

    /// <summary>How much the Mii may look at the cursor right now (it's busy during reactions).</summary>
    public float LookWeight =>
        _reactionClip is null ? 1f
        : _reaction is { } reaction && Important.Contains(reaction) ? 0f
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
            if (_reactionClip is null)
                PlayIdle(FocusFadeSeconds);
            else if (value && !(_reaction is { } reaction && Important.Contains(reaction)))
                EndReaction(playIdle: true, fadeSeconds: FocusFadeSeconds);
        }
    }

    /// <summary>Starts idling now, e.g. after a clip played straight on the player (like falling in) ended.</summary>
    public void Idle()
    {
        if (_reactionClip is not null)
            EndReaction(playIdle: false, fadeSeconds: 0);
        PlayIdle(IdleFadeSeconds);
    }

    /// <summary>Plays the editor's entrance (or just starts idling when there is none).</summary>
    public void Start()
    {
        if (!React(MiiEditorReaction.Enter))
            PlayIdle(0);
    }

    /// <summary>Switches between the whole-Mii and close-up idles. A running reaction plays on.</summary>
    public void SetFocus(MiiEditorFocus focus)
    {
        if (focus == Focus)
            return;
        Focus = focus;
        if (_reactionClip is null)
        {
            PlayIdle(FocusFadeSeconds);
            return;
        }

        // A whole-body reaction would walk out of a close-up; stop it there.
        if (focus == MiiEditorFocus.Face && !IsCloseUp(_reactionPath!) && _reaction is { } reaction && !Important.Contains(reaction))
            EndReaction(playIdle: true, fadeSeconds: FocusFadeSeconds);
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
        if (_reaction is { } playing)
        {
            if (playing == reaction && !important)
                return false;
            if (Important.Contains(playing) && !important)
                return false;
        }

        if (!important && _lastEnded == reaction && _clock.Elapsed - _lastEndedAt < RepeatCooldown)
            return false;

        if (Pick(PoolFor(reaction), reaction.ToString()) is not { } picked)
            return false;

        Begin(reaction, picked);
        return true;
    }

    /// <summary>Whether the playing reaction still has <paramref name="cue"/> ahead.</summary>
    public bool WillCue(string cue) =>
        _reactionClip is { } clip
        && ReferenceEquals(_player.Current, clip)
        && clip.Events.Any(e => e.Name == cue && e.Frame >= _player.Frame);

    private void Begin(MiiEditorReaction reaction, (string Path, MiiAnimation Clip) picked)
    {
        if (_reactionClip is not null)
            EndReaction(playIdle: false, fadeSeconds: 0);
        _reaction = reaction;
        _reactionPath = picked.Path;
        _reactionClip = picked.Clip;
        _player.Play(picked.Clip, loop: false, fadeSeconds: ReactionFadeSeconds);
    }

    private void EndReaction(bool playIdle, double fadeSeconds)
    {
        var ended = _reaction;
        _reactionClip = null;
        _reactionPath = null;
        _reaction = null;
        if (playIdle)
            PlayIdle(fadeSeconds);
        if (ended is { } reaction)
        {
            _lastEnded = reaction;
            _lastEndedAt = _clock.Elapsed;
            ReactionEnded?.Invoke(reaction);
        }
    }

    private void OnClipFinished(MiiAnimation clip)
    {
        if (ReferenceEquals(clip, _reactionClip))
            EndReaction(playIdle: true, fadeSeconds: IdleFadeSeconds);
        else if (ReferenceEquals(clip, _idleClip))
            PlayIdle(IdleFadeSeconds);
    }

    private void OnClipEvent(MiiAnimation clip, AnimEvent animEvent)
    {
        if (ReferenceEquals(clip, _reactionClip) && _reaction is { } reaction)
            Cue?.Invoke(reaction, animEvent.Name);
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

        var idles = _library.List($"{Root}/idle");
        var wanted = idles.Where(path => IsCloseUp(path) == (Focus == MiiEditorFocus.Face)).ToList();
        var picked = Pick(wanted.Count > 0 ? wanted : idles, "idle:" + Focus);
        _idleClip = picked?.Clip;
        if (picked is { } idle)
            _player.Play(idle.Clip, loop: false, fadeSeconds);
        else
            _player.Stop(fadeSeconds);
    }

    /// <summary>Clips for a reaction that suit the current focus (falling back to any of them).</summary>
    private IReadOnlyList<string> PoolFor(MiiEditorReaction reaction)
    {
        var (folder, filter) = Source(reaction);
        var all = _library.List($"{Root}/{folder}").Where(path => filter is null || filter(Path.GetFileName(path))).ToList();
        var suited = all.Where(path => IsCloseUp(path) == (Focus == MiiEditorFocus.Face)).ToList();
        return suited.Count > 0 ? suited : all;
    }

    private static (string Folder, Func<string, bool>? Filter) Source(MiiEditorReaction reaction) =>
        reaction switch
        {
            MiiEditorReaction.Enter => ("enter", null),
            MiiEditorReaction.BodyShape => ("body_shape", null),
            MiiEditorReaction.Name => ("name", null),
            MiiEditorReaction.FavoriteColor => ("favoritecolor", null),
            MiiEditorReaction.Favorite => ("favorite", name => name.StartsWith("Favorite_selected", StringComparison.OrdinalIgnoreCase)),
            MiiEditorReaction.Unfavorite => ("favorite", name => name.StartsWith("Favorite_removed", StringComparison.OrdinalIgnoreCase)),
            MiiEditorReaction.Randomize => ("randomize", null),
            MiiEditorReaction.Save => ("save", null),
            MiiEditorReaction.BecomeGirl => ("gender", name => name.Equals("Become_girl", StringComparison.OrdinalIgnoreCase)),
            MiiEditorReaction.BecomeBoy => ("gender", name => name.Equals("Become_boy", StringComparison.OrdinalIgnoreCase)),
            _ => throw new ArgumentOutOfRangeException(nameof(reaction), reaction, null),
        };

    /// <summary>A random clip from the pool, avoiding the one picked from the same pool last time.</summary>
    private (string Path, MiiAnimation Clip)? Pick(IReadOnlyList<string> pool, string poolKey)
    {
        _lastPicked.TryGetValue(poolKey, out var last);
        var choices = pool.Where(path => path != last || pool.Count == 1).ToList();
        while (choices.Count > 0)
        {
            var path = choices[_random.Next(choices.Count)];
            if (_library.Get(path) is { } clip)
            {
                _lastPicked[poolKey] = path;
                return (path, clip);
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

    private static bool IsCloseUp(string path) => path.EndsWith(CloseUpSuffix, StringComparison.OrdinalIgnoreCase);
}
