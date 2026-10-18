namespace WheelWizard.Views.Shell.Couch.Input;

internal readonly record struct CouchGamepadState(CouchButtons Held, CouchButtons Pressed)
{
    public CouchButtons Buttons => Held | Pressed;
}

/// <summary>Blocks buttons carried across a focus boundary, without delaying fresh presses.</summary>
internal sealed class CouchButtonTracker
{
    private CouchButtons _previousHeld;
    private CouchButtons _blocked;

    public void Reset(CouchButtons held)
    {
        _previousHeld = held;
        _blocked = held;
    }

    public CouchGamepadState Update(CouchGamepadState state, bool scopeChanged)
    {
        if (scopeChanged)
            _blocked |= state.Held & _previousHeld;
        // An SDL down event also catches a release/repress entirely between polls.
        _blocked &= state.Held & ~state.Pressed;
        var pressed = (state.Pressed | (state.Held & ~_previousHeld)) & ~_blocked;
        _previousHeld = state.Held;
        return new CouchGamepadState(state.Held & ~_blocked, pressed);
    }
}
