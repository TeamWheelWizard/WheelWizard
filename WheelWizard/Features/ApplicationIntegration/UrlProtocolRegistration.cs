using WheelWizard.Shared.Platform;

namespace WheelWizard.ApplicationIntegration;

public sealed record UrlProtocolRegistrationState(string? Command, bool HasUrlMarker);

public interface IUrlProtocolRegistrationStore
{
    UrlProtocolRegistrationState? Read(string scheme);
    void Write(string scheme, string command);
}

public interface IUrlProtocolRegistration
{
    OperationResult EnsureRegistered(string? executablePath);
}

public sealed class UrlProtocolRegistration(IUrlProtocolRegistrationStore store, IRuntimeEnvironment environment) : IUrlProtocolRegistration
{
    public const string Scheme = "wheelwizard";

    public OperationResult EnsureRegistered(string? executablePath)
    {
        if (!environment.IsWindows)
            return Ok();
        if (string.IsNullOrWhiteSpace(executablePath))
            return Fail("Cannot register the URL protocol without an executable path.");

        return TryCatch(
            () =>
            {
                var command = $"\"{executablePath}\" \"%1\"";
                var current = store.Read(Scheme);
                if (current?.HasUrlMarker == true && string.Equals(current.Command, command, StringComparison.OrdinalIgnoreCase))
                    return;
                store.Write(Scheme, command);
            },
            "Failed to register the Wheel Wizard URL protocol."
        );
    }
}
