namespace WheelWizard.Shared.Platform;

public interface IRuntimeEnvironment
{
    bool IsLinux { get; }
    bool IsWindows { get; }
    bool IsMacOS { get; }
    System.Runtime.InteropServices.Architecture OSArchitecture { get; }
    string GetFolderPath(System.Environment.SpecialFolder folder);
    string? GetEnvironmentVariable(string name);
}

public sealed class RuntimeEnvironment : IRuntimeEnvironment
{
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public System.Runtime.InteropServices.Architecture OSArchitecture => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

    public string GetFolderPath(System.Environment.SpecialFolder folder) => System.Environment.GetFolderPath(folder);

    public string? GetEnvironmentVariable(string name) => System.Environment.GetEnvironmentVariable(name);
}
