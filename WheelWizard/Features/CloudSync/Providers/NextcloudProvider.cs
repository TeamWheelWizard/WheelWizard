using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace WheelWizard.CloudSync.Providers;

/// <summary>Uses Nextcloud's browser-based Login Flow v2 and stores only its generated app password.</summary>
public sealed class NextcloudProvider(
    IHttpClientFactory clients,
    WheelWizard.Settings.ISettingsManager settings,
    WheelWizard.CloudSync.Credentials.ISecureCredentialStore credentials
) : WebDavProvider(clients, settings, credentials)
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public override CloudProviderType ProviderType => CloudProviderType.Nextcloud;
    protected override string CredentialKey => "cloud-nextcloud";

    public override async Task AuthenticateAsync()
    {
        var server = Settings.Get<string>(Settings.CLOUD_NEXTCLOUD_SERVER).Trim().TrimEnd('/');
        if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUri) || serverUri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Enter a valid Nextcloud server URL, for example https://cloud.example.com.");

        using var startResponse = await Client.PostAsync(new Uri($"{server}/index.php/login/v2"), content: null);
        startResponse.EnsureSuccessStatusCode();
        var start =
            await JsonSerializer.DeserializeAsync<LoginStart>(await startResponse.Content.ReadAsStreamAsync(), JsonOptions)
            ?? throw new InvalidDataException("Nextcloud returned an invalid Login Flow v2 response.");
        if (
            string.IsNullOrWhiteSpace(start.Login)
            || string.IsNullOrWhiteSpace(start.Poll?.Endpoint)
            || string.IsNullOrWhiteSpace(start.Poll.Token)
        )
            throw new InvalidDataException("Nextcloud Login Flow v2 response is incomplete.");

        var browser = Process.Start(new ProcessStartInfo(start.Login) { UseShellExecute = true });
        if (browser is null)
            throw new InvalidOperationException("The default browser could not be opened for Nextcloud login.");

        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            using var pollContent = new FormUrlEncodedContent([new KeyValuePair<string, string>("token", start.Poll.Token)]);
            using var pollResponse = await Client.PostAsync(start.Poll.Endpoint, pollContent);
            if (pollResponse.StatusCode == HttpStatusCode.NotFound)
                continue;
            pollResponse.EnsureSuccessStatusCode();
            var completed =
                await JsonSerializer.DeserializeAsync<LoginResult>(await pollResponse.Content.ReadAsStreamAsync(), JsonOptions)
                ?? throw new InvalidDataException("Nextcloud returned an invalid login result.");
            if (
                string.IsNullOrWhiteSpace(completed.Server)
                || string.IsNullOrWhiteSpace(completed.LoginName)
                || string.IsNullOrWhiteSpace(completed.AppPassword)
            )
                throw new InvalidDataException("Nextcloud login did not return an app password.");

            var canonicalServer = completed.Server.TrimEnd('/');
            await Credentials.SaveAsync(
                CredentialKey,
                new WheelWizard.CloudSync.Credentials.Secret($"{completed.LoginName}:{completed.AppPassword}")
            );
            Settings.Set(Settings.CLOUD_NEXTCLOUD_SERVER, canonicalServer);
            Settings.Set(Settings.CLOUD_REMOTE_ROOT, $"{canonicalServer}/remote.php/dav/files/{Uri.EscapeDataString(completed.LoginName)}");
            return;
        }

        throw new TimeoutException("Nextcloud authorization timed out after 20 minutes. Please try again.");
    }

    private sealed class LoginStart
    {
        public string Login { get; init; } = string.Empty;
        public PollData? Poll { get; init; }
    }

    private sealed class PollData
    {
        public string Token { get; init; } = string.Empty;
        public string Endpoint { get; init; } = string.Empty;
    }

    private sealed class LoginResult
    {
        public string Server { get; init; } = string.Empty;
        public string LoginName { get; init; } = string.Empty;
        public string AppPassword { get; init; } = string.Empty;
    }
}
