using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions;
using WheelWizard.MiiAnimations.Editor;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiAnimations.Playback;

namespace WheelWizard.Test.Features.MiiAnimations;

public class MiiEditorDirectorTests
{
    private readonly MiiAnimationLibrary _library = new(NullLogger<MiiAnimationLibrary>.Instance);
    private readonly MiiAnimationPlayer _player = new();
    private readonly MiiEditorDirector _director;

    public MiiEditorDirectorTests() => _director = new MiiEditorDirector(_player, _library, new RealRandomSystem().Random.New(7));

    private void Run(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1 / 60.0)
        {
            _player.Update(1 / 60.0);
            _player.RaiseEvents();
        }
    }

    [Fact]
    public void Start_PlaysTheEntranceThenIdles()
    {
        _director.Start();
        Assert.Equal(MiiEditorReaction.Enter, _director.Reaction);

        Run(4);

        Assert.Null(_director.Reaction);
        Assert.StartsWith("Editor idle", _player.Current!.Name);
        Assert.DoesNotContain("upper", _player.Current.Name);
    }

    [Fact]
    public void FaceFocus_UsesCloseUpIdles()
    {
        _director.SetFocus(MiiEditorFocus.Face);
        Assert.EndsWith("upper", _player.Current!.Name);
    }

    [Fact]
    public void SameEdit_DoesNotRestartItsReaction()
    {
        Assert.True(_director.React(MiiEditorReaction.BodyShape));
        var clip = _player.Current;

        Assert.False(_director.React(MiiEditorReaction.BodyShape));
        Assert.Same(clip, _player.Current);
    }

    [Fact]
    public void GenderSwap_CuesTheSwapAndIgnoresEdits()
    {
        var cues = new List<string>();
        _director.Cue += (_, cue) => cues.Add(cue);

        Assert.True(_director.React(MiiEditorReaction.BecomeGirl));
        Assert.True(_director.WillCue(MiiEditorCues.SwapGender));
        Assert.False(_director.React(MiiEditorReaction.FavoriteColor));

        Run(1);

        Assert.Contains(MiiEditorCues.SwapGender, cues);
        Assert.False(_director.WillCue(MiiEditorCues.SwapGender));
    }

    [Fact]
    public void ReactionEnded_FiresWhenAReactionIsReplaced()
    {
        var ended = new List<MiiEditorReaction>();
        _director.ReactionEnded += ended.Add;

        _director.React(MiiEditorReaction.Randomize);
        _director.React(MiiEditorReaction.Randomize);

        Assert.Equal([MiiEditorReaction.Randomize], ended);
    }

    [Fact]
    public void EveryReaction_HasAClip()
    {
        foreach (var reaction in Enum.GetValues<MiiEditorReaction>())
        {
            var director = new MiiEditorDirector(new MiiAnimationPlayer(), _library, new RealRandomSystem().Random.New(1));
            Assert.True(director.React(reaction), reaction.ToString());
        }
    }
}
