using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WheelWizard.Shared.Processes;

public interface IApplicationProcess
{
    string? ExecutablePath { get; }
    int Id { get; }
    string? NativeLibrarySearchDirectories { get; }
    string WorkingDirectory { get; }
    Architecture Architecture { get; }
    bool IsAdministrator { get; }
    void Exit(int exitCode);
}

public sealed class ApplicationProcess : IApplicationProcess
{
    public string? ExecutablePath => Environment.ProcessPath;
    public int Id => Environment.ProcessId;
    public string? NativeLibrarySearchDirectories => AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
    public string WorkingDirectory => Environment.CurrentDirectory;
    public Architecture Architecture => RuntimeInformation.ProcessArchitecture;
    public bool IsAdministrator
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void Exit(int exitCode) => Environment.Exit(exitCode);
}
