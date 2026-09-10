using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// The Linux stand-in for the Windows setup's <c>--check-products</c>. The AppImage only prints a
/// human-readable table, so Wheel Wizard reconstructs the same <see cref="RecompProductsEvent"/> from
/// what the AppImage leaves on disk: its <c>install-state.json</c> (which products exist and where) and
/// each product's <c>local-build.json</c> (which Code.pul it was compiled against). Anything that cannot
/// be verified reads as needing action, never as current.
/// </summary>
public sealed class RecompLinuxProductInspector(IFileSystem fileSystem, ILogger<RecompLinuxProductInspector> logger)
{
    public const string BaseProfile = "base";
    public const string RetroRewindProfile = "retro-rewind";
    public const string BuildRecordFileName = "local-build.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Reports the state of both products.
    /// </summary>
    /// <param name="backendStateFilePath">The AppImage's own <c>install-state.json</c>.</param>
    /// <param name="setupVersion">The installed setup version Wheel Wizard recorded, echoed into the report.</param>
    /// <param name="installFolderPath">The backend folder, echoed into the report as its install directory.</param>
    /// <param name="retroRewindFolderPath">
    /// Wheel Wizard's <c>RetroRewind6</c> folder, whose <c>Binaries/Code.pul</c> the Retro Rewind product must
    /// have been built from. <see langword="null"/> when Retro Rewind is not installed, in which case the
    /// Retro Rewind product is only checked for presence.
    /// </param>
    public RecompProductsEvent Inspect(string backendStateFilePath, string? setupVersion, string installFolderPath, string? retroRewindFolderPath)
    {
        var state = ReadBackendState(backendStateFilePath, out var stateMalformed);
        if (stateMalformed)
        {
            var malformed = new RecompProductStatus(RecompProductState.Unknown, "The WiiCompiled install state could not be read.", ProtocolValid: false);
            return new(setupVersion, installFolderPath, RebuildRequired: false, malformed, malformed, ProtocolValid: false);
        }

        var @base = InspectPresence(state, BaseProfile);
        var retroRewind = InspectPresence(state, RetroRewindProfile);
        if (retroRewind.IsCurrent && retroRewindFolderPath is not null)
            retroRewind = InspectCodePul(FindRecord(state, RetroRewindProfile)!, retroRewindFolderPath);

        return new(setupVersion, installFolderPath, RebuildRequired: false, @base, retroRewind);
    }

    private RecompProductStatus InspectPresence(RecompLinuxBackendState? state, string profile)
    {
        var record = FindRecord(state, profile);
        if (record is null)
            return new(RecompProductState.Absent, $"The {profile} product has not been built.");

        if (string.IsNullOrWhiteSpace(record.InstallDirectory) || string.IsNullOrWhiteSpace(record.ExecutableName))
            return new(RecompProductState.Broken, $"The {profile} product record does not say where its executable is.");

        var executablePath = fileSystem.Path.Combine(record.InstallDirectory, record.ExecutableName);
        return fileSystem.File.Exists(executablePath)
            ? new(RecompProductState.Current, "ok")
            : new(RecompProductState.Broken, $"The {profile} executable is missing at {executablePath}.");
    }

    /// <summary>
    /// Retro Rewind is compiled together with its Code.pul, so a new Code.pul means the product on disk
    /// no longer is Retro Rewind as installed. A build whose provenance cannot be read is treated the
    /// same way: rebuilding is the only way to know.
    /// </summary>
    private RecompProductStatus InspectCodePul(RecompLinuxProductRecord record, string retroRewindFolderPath)
    {
        var codePulPath = fileSystem.Path.Combine(retroRewindFolderPath, "Binaries", "Code.pul");
        if (!fileSystem.File.Exists(codePulPath))
            return new(RecompProductState.InputsMissing, $"Retro Rewind has no Code.pul at {codePulPath}.");

        var buildRecord = ReadBuildRecord(fileSystem.Path.Combine(record.InstallDirectory!, BuildRecordFileName));
        if (string.IsNullOrWhiteSpace(buildRecord?.CodePulSha256))
            return new(RecompProductState.CodePulChanged, "The Retro Rewind product does not record which Code.pul it was built from.");

        string currentSha;
        try
        {
            using var stream = fileSystem.File.OpenRead(codePulPath);
            currentSha = Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not hash the Retro Rewind Code.pul at {Path}", codePulPath);
            return new(RecompProductState.InputsMissing, "The Retro Rewind Code.pul could not be read.");
        }

        return string.Equals(currentSha, buildRecord.CodePulSha256, StringComparison.OrdinalIgnoreCase)
            ? new(RecompProductState.Current, "ok")
            : new(RecompProductState.CodePulChanged, "Code.pul changed since the Retro Rewind product was built.");
    }

    private static RecompLinuxProductRecord? FindRecord(RecompLinuxBackendState? state, string profile) =>
        state?.Products.FirstOrDefault(record => string.Equals(record.Profile, profile, StringComparison.OrdinalIgnoreCase));

    private RecompLinuxBackendState? ReadBackendState(string path, out bool malformed)
    {
        malformed = false;
        try
        {
            if (!fileSystem.File.Exists(path))
                return null;

            var state = JsonSerializer.Deserialize<RecompLinuxBackendState>(fileSystem.File.ReadAllText(path), JsonOptions);
            malformed = state is null;
            return state;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the WiiCompiled install state at {Path}", path);
            malformed = true;
            return null;
        }
    }

    private RecompLinuxBuildRecord? ReadBuildRecord(string path)
    {
        try
        {
            if (!fileSystem.File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<RecompLinuxBuildRecord>(fileSystem.File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the WiiCompiled build record at {Path}", path);
            return null;
        }
    }
}
