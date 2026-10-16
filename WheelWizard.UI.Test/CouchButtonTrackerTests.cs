using WheelWizard.Views.Shell.Couch.Input;

namespace WheelWizard.UI.Test;

public class CouchButtonTrackerTests
{
    [Fact]
    public void ScopeChange_BlocksHeldConfirmButAcceptsFreshBackImmediately()
    {
        var input = new CouchButtonTracker();
        input.Reset(CouchButtons.None);
        Assert.Equal(CouchButtons.Confirm, input.Update(new(CouchButtons.Confirm, CouchButtons.Confirm), false).Pressed);
        var next = input.Update(new(CouchButtons.Confirm | CouchButtons.Back, CouchButtons.Back), true);
        Assert.Equal(CouchButtons.Back, next.Pressed);
        Assert.Equal(CouchButtons.Back, next.Held);
        Assert.Equal(CouchButtons.None, input.Update(new(CouchButtons.Confirm, CouchButtons.None), false).Pressed);
    }

    [Fact]
    public void ScopeChange_AcceptsNewConfirmAfterReleaseWithoutANeutralDelay()
    {
        var input = new CouchButtonTracker();
        input.Reset(CouchButtons.None);
        input.Update(new(CouchButtons.Confirm, CouchButtons.Confirm), false);
        input.Update(default, true);
        Assert.Equal(CouchButtons.Confirm, input.Update(new(CouchButtons.Confirm, CouchButtons.Confirm), false).Pressed);
    }

    [Fact]
    public void Activation_BlocksButtonsHeldInTheBackgroundUntilReleased()
    {
        var input = new CouchButtonTracker();
        input.Reset(CouchButtons.Confirm | CouchButtons.Right);
        Assert.Equal(default, input.Update(new(CouchButtons.Confirm | CouchButtons.Right, CouchButtons.None), false));
        input.Update(default, false);
        Assert.Equal(CouchButtons.Confirm, input.Update(new(CouchButtons.Confirm, CouchButtons.Confirm), false).Pressed);
    }

    [Fact]
    public void ShortPresses_AreNotLostBetweenPollingFramesOrMergedWithThePreviousPress()
    {
        var input = new CouchButtonTracker();
        input.Reset(CouchButtons.None);
        Assert.Equal(CouchButtons.Confirm, input.Update(new(CouchButtons.None, CouchButtons.Confirm), false).Pressed);
        Assert.Equal(CouchButtons.Confirm, input.Update(new(CouchButtons.None, CouchButtons.Confirm), false).Pressed);
        Assert.Equal(CouchButtons.None, input.Update(default, false).Pressed);
    }

    [Fact]
    public void ReleaseAndRepressBetweenFrames_UnblocksAButtonCarriedAcrossScopeChange()
    {
        var input = new CouchButtonTracker();
        input.Reset(CouchButtons.Confirm);
        Assert.Equal(CouchButtons.None, input.Update(new(CouchButtons.Confirm, CouchButtons.None), true).Pressed);
        Assert.Equal(CouchButtons.Confirm, input.Update(new(CouchButtons.Confirm, CouchButtons.Confirm), false).Pressed);
    }
}
