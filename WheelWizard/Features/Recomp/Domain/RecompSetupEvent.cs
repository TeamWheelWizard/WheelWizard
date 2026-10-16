namespace WheelWizard.Recomp.Domain;

/// <summary>
/// A single line of the NDJSON stream that the recomp setup executable writes when run with <c>--progress-json</c>.
/// </summary>
public abstract record RecompSetupEvent;

/// <summary>
/// <c>{"type":"progress","stage":"...","message":"...","percent":0-100}</c>
/// The percent is monotonic and stays below 100 until the result line arrives.
/// </summary>
public sealed record RecompSetupProgressEvent(string Stage, string Message, int Percent) : RecompSetupEvent;

/// <summary>
/// The terminal line of the stream, guaranteed exactly once on every exit path:
/// <c>{"type":"result","success":true,"version":"...","installDir":"..."}</c> or
/// <c>{"type":"result","success":false,"error":"..."}</c>
/// </summary>
public sealed record RecompSetupResultEvent(bool Success, string? Version, string? InstallDir, string? Error) : RecompSetupEvent;

/// <summary>
/// The <c>{"type":"products",...}</c> line emitted by <c>--check-products</c>. It is the authoritative
/// answer about whether the installed products still match their compile inputs and cached payload.
/// </summary>
public sealed record RecompProductsEvent(
    string? SetupVersion,
    string? InstallDir,
    bool RebuildRequired,
    RecompProductStatus Base,
    RecompProductStatus RetroRewind,
    bool ProtocolValid = true
) : RecompSetupEvent
{
    /// <summary>
    /// Whether the report calls for any repair action. Product-level state is included so a malformed
    /// or newer report can never be mistaken for a clean installation merely because
    /// <see cref="RebuildRequired"/> was absent or false.
    /// </summary>
    public bool ActionRequired => !ProtocolValid || RebuildRequired || Base.ActionRequired || RetroRewind.ActionRequired;

    /// <summary>Whether either product is in a state that must not be launched.</summary>
    public bool IsBlocked => !ProtocolValid || Base.IsBlocked || RetroRewind.IsBlocked;
}

/// <summary>The product statuses of the v1 contract. Anything else is <see cref="Unknown"/>.</summary>
public enum RecompProductState
{
    Unknown,
    Absent,
    Current,
    ToolkitChanged,
    CodePulChanged,
    CompileInputsChanged,
    PayloadChanged,
    InputsMissing,
    Blocked,
    Broken,
}

public sealed record RecompProductStatus(RecompProductState State, string Detail, bool ProtocolValid = true)
{
    public static RecompProductStatus Unknown { get; } =
        new(RecompProductState.Unknown, "The setup did not report product status.", ProtocolValid: false);

    public bool IsCurrent => ProtocolValid && State == RecompProductState.Current;
    public bool ActionRequired => !ProtocolValid || State is not (RecompProductState.Absent or RecompProductState.Current);

    public bool RequiresCompile =>
        ProtocolValid
        && State is RecompProductState.ToolkitChanged or RecompProductState.CodePulChanged or RecompProductState.CompileInputsChanged;

    public bool IsBlocked =>
        !ProtocolValid
        || State
            is RecompProductState.Unknown
                or RecompProductState.InputsMissing
                or RecompProductState.Blocked
                or RecompProductState.Broken;

    public static RecompProductState ParseState(string? status) =>
        (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "absent" => RecompProductState.Absent,
            "current" => RecompProductState.Current,
            "toolkit-changed" => RecompProductState.ToolkitChanged,
            "code-pul-changed" => RecompProductState.CodePulChanged,
            "compile-inputs-changed" => RecompProductState.CompileInputsChanged,
            "payload-changed" => RecompProductState.PayloadChanged,
            "inputs-missing" => RecompProductState.InputsMissing,
            "blocked" => RecompProductState.Blocked,
            "broken" => RecompProductState.Broken,
            _ => RecompProductState.Unknown,
        };
}
