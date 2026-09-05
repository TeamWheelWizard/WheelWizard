namespace WheelWizard.Recomp.Domain;

/// <summary>
/// The <c>install-state.json</c> the Linux AppImage keeps in its own data directory. It is a flat list of
/// the products it built and where; it does not record the setup version, which is why Wheel Wizard
/// writes its own <see cref="RecompInstallState"/> next to the installed AppImage copy.
/// </summary>
public sealed class RecompLinuxBackendState
{
    public int? SchemaVersion { get; set; }
    public string? Workspace { get; set; }
    public List<RecompLinuxProductRecord> Products { get; set; } = [];
}

/// <summary>One product the AppImage built. <c>Profile</c> is <c>base</c> or <c>retro-rewind</c>.</summary>
public sealed class RecompLinuxProductRecord
{
    public string? Profile { get; set; }
    public string? InstallDirectory { get; set; }
    public string? ExecutableName { get; set; }
    public string? DolSha256 { get; set; }
    public string? RelSha256 { get; set; }
    public string? BuiltUtc { get; set; }
}

/// <summary>
/// The <c>local-build.json</c> the AppImage's build script leaves beside each product. Its
/// <c>CodePulSha256</c> is what tells whether the Retro Rewind product still matches the installed Code.pul.
/// </summary>
public sealed class RecompLinuxBuildRecord
{
    public int? SchemaVersion { get; set; }
    public string? Profile { get; set; }
    public string? DolSha256 { get; set; }
    public string? RelSha256 { get; set; }
    public string? CodePulSha256 { get; set; }
}
