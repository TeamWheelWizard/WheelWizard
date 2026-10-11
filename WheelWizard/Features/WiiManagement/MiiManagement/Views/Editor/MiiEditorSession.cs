using System.Diagnostics;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Editor;

/// <summary>
/// The Mii being edited, with undo/redo and whether it differs from how it started (to warn before leaving).
/// Every change goes through <see cref="Change"/>, which keeps the state before it for undo.
/// </summary>
public sealed class MiiEditorSession
{
    /// <summary>Changes with the same key this close together are undone as one (e.g. dragging a slider).</summary>
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(1.2);

    private readonly Stack<Mii> _undo = new();
    private readonly Stack<Mii> _redo = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private byte[] _original;
    private string? _lastKey;
    private TimeSpan _lastChangeAt;

    public MiiEditorSession(Mii mii, bool isNew)
    {
        Mii = mii;
        IsNew = isNew;
        _original = Bytes(mii);
    }

    /// <summary>The Mii as it is now. Replace it only through <see cref="Change"/>, <see cref="Undo"/> or <see cref="Redo"/>.</summary>
    public Mii Mii { get; private set; }

    /// <summary>A Mii that isn't in the database yet.</summary>
    public bool IsNew { get; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Whether the Mii differs from how it started (undoing back to the start makes it clean again).</summary>
    public bool HasChanges => !Bytes(Mii).AsSpan().SequenceEqual(_original);

    /// <summary>Fired after every change, undo and redo.</summary>
    public event Action? Changed;

    /// <summary>Starts over with this Mii as the unchanged state (e.g. the Mii picked for a new Mii).</summary>
    public void Reset(Mii mii)
    {
        Mii = mii;
        _original = Bytes(mii);
        _undo.Clear();
        _redo.Clear();
        _lastKey = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Applies <paramref name="edit"/> to a copy of the Mii. When it returns true and the Mii really changed, the copy
    /// becomes the Mii and the old one goes on the undo stack. Returns whether the Mii changed.
    /// </summary>
    public bool Change(Func<Mii, bool> edit, string? mergeKey = null)
    {
        if (Copy(Mii) is not { } copy || !edit(copy))
            return false;
        if (Bytes(copy).AsSpan().SequenceEqual(Bytes(Mii)))
            return false;

        var now = _clock.Elapsed;
        var merge = mergeKey is not null && mergeKey == _lastKey && now - _lastChangeAt < MergeWindow && _undo.Count > 0;
        if (!merge)
            _undo.Push(Mii);
        _redo.Clear();
        _lastKey = mergeKey;
        _lastChangeAt = now;
        Mii = copy;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ends merging, so the next change with the same key gets its own undo step (e.g. a new drag).</summary>
    public void EndMerge() => _lastKey = null;

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        _redo.Push(Mii);
        Mii = _undo.Pop();
        _lastKey = null;
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        _undo.Push(Mii);
        Mii = _redo.Pop();
        _lastKey = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The state before the last change (for showing what changed), or null.</summary>
    public Mii? Previous => _undo.Count > 0 ? _undo.Peek() : null;

    public static Mii? Copy(Mii mii) => mii.Clone() is { IsSuccess: true } copy ? copy.Value : null;

    private static byte[] Bytes(Mii mii) => MiiSerializer.Serialize(mii) is { IsSuccess: true } bytes ? bytes.Value : [];
}
