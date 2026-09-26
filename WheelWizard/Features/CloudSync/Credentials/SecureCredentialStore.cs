using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WheelWizard.CloudSync.Credentials;

/// <summary>
/// The common, fail-closed credential boundary. Secrets are deliberately never serialized into
/// Wheel Wizard settings, manifests or profile archives. Windows uses Credential Manager and Linux
/// uses the user's libsecret service via secret-tool; a missing keychain is an error, not an
/// insecure file fallback.
/// </summary>
public sealed class SecureCredentialStore : ISecureCredentialStore
{
    public Task SaveAsync(string key, Secret value) =>
        OperatingSystem.IsWindows() ? Task.Run(() => WriteWindows(key, value.Value))
        : OperatingSystem.IsLinux() ? RunSecretToolAsync("store", key, value.Value)
        : throw Unsupported();

    public Task<Secret?> GetAsync(string key) =>
        OperatingSystem.IsWindows() ? Task.Run(() => ReadWindows(key))
        : OperatingSystem.IsLinux() ? ReadSecretToolAsync(key)
        : throw Unsupported();

    public Task DeleteAsync(string key) =>
        OperatingSystem.IsWindows() ? Task.Run(() => DeleteWindows(key))
        : OperatingSystem.IsLinux() ? RunSecretToolAsync("clear", key, null)
        : throw Unsupported();

    private static Exception Unsupported() =>
        new PlatformNotSupportedException("No supported OS credential store is available for cloud credentials.");

    private static string Target(string key) => "WheelWizard.CloudSync/" + key;

    private static async Task RunSecretToolAsync(string command, string key, string? secret)
    {
        using var process = StartSecretTool(command, key);
        if (secret is not null)
            await process.StandardInput.WriteAsync(secret);
        process.StandardInput.Close();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The Linux credential store rejected the request: {error.Trim()}");
    }

    private static async Task<Secret?> ReadSecretToolAsync(string key)
    {
        using var process = StartSecretTool("lookup", key);
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode == 1)
            return null;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The Linux credential store rejected the request: {error.Trim()}");
        return string.IsNullOrEmpty(output) ? null : new Secret(output.TrimEnd('\r', '\n'));
    }

    private static Process StartSecretTool(string command, string key)
    {
        var start = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(command);
        if (command == "store")
            start.ArgumentList.Add("--label=WheelWizard Cloud Sync");
        start.ArgumentList.Add("service");
        start.ArgumentList.Add("wheelwizard-cloud");
        start.ArgumentList.Add("key");
        start.ArgumentList.Add(key);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the Linux credential store.");
    }

    private static void WriteWindows(string key, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = 1,
                TargetName = Target(key),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = 2,
                UserName = "WheelWizard",
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("Windows Credential Manager could not save the cloud credential.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static Secret? ReadWindows(string key)
    {
        if (!CredRead(Target(key), 1, 0, out var pointer))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            if (bytes.Length > 0)
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return new Secret(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void DeleteWindows(string key)
    {
        if (!CredDelete(Target(key), 1, 0))
        { /* absent is a valid disconnect */
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32", SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
