namespace WheelWizard.Shared.Platform;

public interface IRuntimeEnvironment
{
    bool IsLinux { get; }
    bool IsWindows { get; }
    bool IsMacOS { get; }
    string GetFolderPath(System.Environment.SpecialFolder folder);
    string? GetEnvironmentVariable(string name);
}

public sealed class RuntimeEnvironment : IRuntimeEnvironment
{
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string GetFolderPath(System.Environment.SpecialFolder folder) => System.Environment.GetFolderPath(folder);

    public string? GetEnvironmentVariable(string name) => System.Environment.GetEnvironmentVariable(name);
}
