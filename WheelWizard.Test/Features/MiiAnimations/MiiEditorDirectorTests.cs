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
        var entered = _director.WhenReached();
        Assert.False(entered.IsCompleted);

        Run(4);

        Assert.True(entered.IsCompleted);
        Assert.StartsWith("Editor idle", _player.Current!.Name);
    }

    [Fact]
    public void HoldStill_StopsEditReactions()
    {
        _director.HoldStill = true;
        Assert.False(_director.React(MiiEditorReaction.BodyShape));
        Assert.True(_director.React(MiiEditorReaction.BecomeGirl));
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
    public void GenderSwap_ReachesTheSwapAndIgnoresEdits()
    {
        Assert.True(_director.React(MiiEditorReaction.BecomeGirl));
        var swapped = _director.WhenReached(MiiEditorCues.SwapGender);
        Assert.False(swapped.IsCompleted);
        Assert.False(_director.React(MiiEditorReaction.FavoriteColor));

        Run(1);

        Assert.True(swapped.IsCompleted);
        Assert.True(_director.WhenReached(MiiEditorCues.SwapGender).IsCompleted);
    }

    [Fact]
    public void WhenReached_CompletesForCuesTheClipDoesNotHave()
    {
        Assert.True(_director.React(MiiEditorReaction.BecomeGirl));
        Assert.True(_director.WhenReached("no_such_cue").IsCompleted);
    }

    [Fact]
    public void WhenReached_CompletesWhenTheReactionIsReplaced()
    {
        _director.React(MiiEditorReaction.Randomize);
        var first = _director.WhenReached(MiiEditorCues.Randomize);
        Assert.False(first.IsCompleted);

        _director.React(MiiEditorReaction.Randomize);

        Assert.True(first.IsCompleted);
        Assert.False(_director.WhenReached().IsCompleted);
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
