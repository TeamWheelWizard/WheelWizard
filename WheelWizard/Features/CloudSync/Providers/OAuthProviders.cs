using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WheelWizard.CloudSync.Credentials;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.CloudSync.Providers;

/// <summary>
/// Common OAuth 2.0 authorization-code-with-PKCE support for public desktop clients. Client ids
/// are settings (and therefore non-secret); access and refresh tokens never leave the OS credential store.
/// </summary>
public abstract class OAuthCloudProvider(IHttpClientFactory clients, ISettingsManager settings, ISecureCredentialStore credentials)
    : ICloudProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public abstract CloudProviderType ProviderType { get; }
    protected abstract string CredentialKey { get; }
    protected abstract Setting ClientIdSetting { get; }
    protected abstract string AuthorizationEndpoint { get; }
    protected abstract string TokenEndpoint { get; }
    protected abstract IReadOnlyList<string> Scopes { get; }

    protected HttpClient Client => clients.CreateClient("WheelWizard.CloudSync.OAuth");
    protected string ClientId => settings.Get<string>(ClientIdSetting).Trim();

    public async Task AuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException($"Enter the public {ProviderDisplayName} OAuth client ID before signing in.");

        using var listener = LoopbackListener.Start();
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizationUri = BuildAuthorizationUri(listener.RedirectUri, state, challenge);
        if (Process.Start(new ProcessStartInfo(authorizationUri) { UseShellExecute = true }) is null)
            throw new InvalidOperationException("The default browser could not be opened for cloud sign-in.");

        var callback = await listener.WaitForCallbackAsync(TimeSpan.FromMinutes(10));
        var callbackParameters = ParseQuery(callback.Url?.Query);
        var error = callbackParameters.GetValueOrDefault("error");
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"{ProviderDisplayName} sign-in was cancelled or denied: {error}.");
        if (
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state),
                Encoding.UTF8.GetBytes(callbackParameters.GetValueOrDefault("state") ?? string.Empty)
            )
        )
            throw new InvalidOperationException("The OAuth sign-in response did not match the request. Please try again.");
        var code = callbackParameters.GetValueOrDefault("code");
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException($"{ProviderDisplayName} sign-in did not return an authorization code.");

        var values = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = listener.RedirectUri,
            ["code_verifier"] = verifier,
        };
        var token = await RequestTokenAsync(values);
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidDataException(
                $"{ProviderDisplayName} did not return a reusable refresh token. Remove this app from the provider account and sign in again."
            );
        await SaveTokenAsync(token);
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        try
        {
            _ = await GetAccessTokenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task DisconnectAsync() => credentials.DeleteAsync(CredentialKey);

    protected async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool retryUnauthorized = true)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());
        var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !retryUnauthorized)
            return response;

        response.Dispose();
        // Requests with bodies are deliberately not replayed. A failed resumable upload can safely be retried by the caller.
        throw new UnauthorizedAccessException($"{ProviderDisplayName} rejected the saved authorization. Sign in again.");
    }

    protected async Task<string> GetAccessTokenAsync()
    {
        var stored =
            await credentials.GetAsync(CredentialKey) ?? throw new InvalidOperationException($"Sign in to {ProviderDisplayName} first.");
        var token =
            JsonSerializer.Deserialize<OAuthToken>(stored.Value, JsonOptions)
            ?? throw new InvalidDataException($"The saved {ProviderDisplayName} authorization is invalid.");
        if (!string.IsNullOrWhiteSpace(token.AccessToken) && token.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            return token.AccessToken;
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException($"The saved {ProviderDisplayName} authorization has expired. Sign in again.");

        var refreshed = await RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken,
            }
        );
        if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
            throw new InvalidOperationException($"{ProviderDisplayName} could not refresh its authorization. Sign in again.");
        refreshed = refreshed with
        {
            RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? token.RefreshToken : refreshed.RefreshToken,
        };
        await SaveTokenAsync(refreshed);
        return refreshed.AccessToken;
    }

    private async Task<OAuthToken> RequestTokenAsync(IReadOnlyDictionary<string, string> values)
    {
        using var response = await Client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(values));
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{ProviderDisplayName} authorization failed ({(int)response.StatusCode}): {details}");
        }
        var responseToken =
            await JsonSerializer.DeserializeAsync<TokenResponse>(await response.Content.ReadAsStreamAsync(), JsonOptions)
            ?? throw new InvalidDataException($"{ProviderDisplayName} returned an invalid token response.");
        return new OAuthToken(
            responseToken.AccessToken,
            responseToken.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(responseToken.ExpiresIn, 60))
        );
    }

    private Task SaveTokenAsync(OAuthToken token) => credentials.SaveAsync(CredentialKey, new Secret(JsonSerializer.Serialize(token)));

    private string BuildAuthorizationUri(string redirectUri, string state, string challenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', Scopes),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        AddAuthorizationParameters(query);
        return AuthorizationEndpoint
            + "?"
            + string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    protected virtual void AddAuthorizationParameters(IDictionary<string, string> query) { }

    protected virtual string ProviderDisplayName => ProviderType == CloudProviderType.GoogleDrive ? "Google Drive" : "OneDrive";

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static IReadOnlyDictionary<string, string> ParseQuery(string? query) =>
        query
            ?.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length > 0)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty,
                StringComparer.Ordinal
            ) ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public abstract Task<RemoteFileInfo?> GetFileInfoAsync(string path);
    public abstract Task DownloadAsync(string remotePath, string localPath);
    public abstract Task UploadAsync(string localPath, string remotePath);

    public async Task<bool> ExistsAsync(string path) => await GetFileInfoAsync(path) is not null;

    public abstract Task<IReadOnlyList<string>> ListAsync(string path);

    private sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);

    private sealed record OAuthToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresUtc);

    private sealed class LoopbackListener : IDisposable
    {
        private readonly HttpListener _listener = new();

        private LoopbackListener(int port)
        {
            RedirectUri = $"http://127.0.0.1:{port}/callback/";
            _listener.Prefixes.Add(RedirectUri);
            _listener.Start();
        }

        public string RedirectUri { get; }

        public static LoopbackListener Start()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            return new LoopbackListener(port);
        }

        public async Task<HttpListenerRequest> WaitForCallbackAsync(TimeSpan timeout)
        {
            using var cancel = new CancellationTokenSource(timeout);
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancel.Token);
                const string page =
                    "<html><body><h2>WheelWizard sign-in complete</h2><p>You can return to WheelWizard and close this tab.</p></body></html>";
                var bytes = Encoding.UTF8.GetBytes(page);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
                return context.Request;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Cloud browser sign-in timed out after 10 minutes. Please try again.");
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}

/// <summary>Google Drive transport confined to the OAuth application's private appDataFolder.</summary>
public sealed class GoogleDriveProvider : OAuthCloudProvider
{
    private const string DriveApi = "https://www.googleapis.com/drive/v3";
    private const string UploadApi = "https://www.googleapis.com/upload/drive/v3";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private readonly ISettingsManager _settings;

    public GoogleDriveProvider(IHttpClientFactory clients, ISettingsManager settings, ISecureCredentialStore credentials)
        : base(clients, settings, credentials) => _settings = settings;

    public override CloudProviderType ProviderType => CloudProviderType.GoogleDrive;
    protected override string CredentialKey => "cloud-google-drive";
    protected override Setting ClientIdSetting => _settings.CLOUD_GOOGLE_CLIENT_ID;
    protected override string AuthorizationEndpoint => "https://accounts.google.com/o/oauth2/v2/auth";
    protected override string TokenEndpoint => "https://oauth2.googleapis.com/token";
    protected override IReadOnlyList<string> Scopes => ["https://www.googleapis.com/auth/drive.appdata"];

    protected override void AddAuthorizationParameters(IDictionary<string, string> query)
    {
        query["access_type"] = "offline";
        // Google otherwise omits a refresh token when the account has approved this app before.
        query["prompt"] = "consent";
    }

    public override async Task<RemoteFileInfo?> GetFileInfoAsync(string path)
    {
        var item = await FindItemAsync(path);
        return item is null ? null : new RemoteFileInfo(path, item.Size, item.ModifiedUtc, item.ETag);
    }

    public override async Task DownloadAsync(string remotePath, string localPath)
    {
        var item =
            await FindItemAsync(remotePath) ?? throw new FileNotFoundException("The Google Drive cloud file does not exist.", remotePath);
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{DriveApi}/files/{item.Id}?alt=media"));
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await using var output = File.Create(localPath);
        await response.Content.CopyToAsync(output);
    }

    public override async Task UploadAsync(string localPath, string remotePath)
    {
        var segments = Segments(remotePath);
        if (segments.Length == 0)
            throw new InvalidOperationException("A Google Drive cloud file path is required.");
        var parent = await EnsureFoldersAsync(segments[..^1]);
        var existing = await FindChildAsync(parent, segments[^1]);
        var metadata = JsonSerializer.Serialize(new { name = segments[^1], parents = existing is null ? new[] { parent } : null });
        await using var input = File.OpenRead(localPath);
        using var create = new HttpRequestMessage(
            existing is null ? HttpMethod.Post : HttpMethod.Patch,
            existing is null ? $"{UploadApi}/files?uploadType=resumable" : $"{UploadApi}/files/{existing.Id}?uploadType=resumable"
        );
        create.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
        create.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "application/octet-stream");
        create.Headers.TryAddWithoutValidation("X-Upload-Content-Length", input.Length.ToString());
        using var session = await SendAsync(create);
        session.EnsureSuccessStatusCode();
        var uploadUri = session.Headers.Location ?? throw new InvalidDataException("Google Drive did not create an upload session.");
        await UploadChunksAsync(uploadUri, input, input.Length);
    }

    public override async Task<IReadOnlyList<string>> ListAsync(string path)
    {
        var folder = await FindItemAsync(path);
        if (folder is null)
            return [];
        var children = await FindChildrenAsync(folder.Id);
        var prefix = "/" + string.Join('/', Segments(path));
        return children.Select(child => $"{prefix}/{Uri.EscapeDataString(child.Name)}").ToList();
    }

    private async Task UploadChunksAsync(Uri uploadUri, Stream input, long length)
    {
        const int chunkSize = 256 * 1024;
        var buffer = new byte[chunkSize];
        long position = 0;
        while (position < length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(chunkSize, length - position)));
            if (read == 0)
                throw new EndOfStreamException("The local upload file ended unexpectedly.");
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
            request.Content = new ByteArrayContent(buffer, 0, read);
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(position, position + read - 1, length);
            using var response = await Client.SendAsync(request);
            if (position + read < length && response.StatusCode != HttpStatusCode.PermanentRedirect)
                response.EnsureSuccessStatusCode();
            if (position + read == length)
                response.EnsureSuccessStatusCode();
            position += read;
        }
    }

    private async Task<DriveItem?> FindItemAsync(string path)
    {
        var parent = "appDataFolder";
        DriveItem? result = null;
        foreach (var segment in Segments(path))
        {
            result = await FindChildAsync(parent, segment);
            if (result is null)
                return null;
            parent = result.Id;
        }
        return result;
    }

    private async Task<string> EnsureFoldersAsync(IReadOnlyList<string> segments)
    {
        var parent = "appDataFolder";
        foreach (var segment in segments)
        {
            var existing = await FindChildAsync(parent, segment);
            if (existing is not null)
            {
                if (!string.Equals(existing.MimeType, FolderMimeType, StringComparison.Ordinal))
                    throw new IOException($"Google Drive item '{segment}' is a file, not a folder.");
                parent = existing.Id;
                continue;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DriveApi}/files")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        new
                        {
                            name = segment,
                            mimeType = FolderMimeType,
                            parents = new[] { parent },
                        }
                    ),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            using var response = await SendAsync(request);
            response.EnsureSuccessStatusCode();
            parent = (await ReadItemAsync(response))?.Id ?? throw new InvalidDataException("Google Drive did not create a folder.");
        }
        return parent;
    }

    private async Task<DriveItem?> FindChildAsync(string parent, string name)
    {
        var escapedName = name.Replace("'", "\\'", StringComparison.Ordinal);
        var query = Uri.EscapeDataString($"name = '{escapedName}' and '{parent}' in parents and trashed = false");
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"{DriveApi}/files?q={query}&spaces=appDataFolder&fields=files(id,name,mimeType,size,modifiedTime,etag)&pageSize=2"
            )
        );
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0 ? ReadItem(files[0]) : null;
    }

    private async Task<IReadOnlyList<DriveItem>> FindChildrenAsync(string parent)
    {
        var query = Uri.EscapeDataString($"'{parent}' in parents and trashed = false");
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"{DriveApi}/files?q={query}&spaces=appDataFolder&fields=files(id,name,mimeType,size,modifiedTime,etag)&pageSize=1000"
            )
        );
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.TryGetProperty("files", out var files) ? files.EnumerateArray().Select(ReadItem).ToList() : [];
    }

    private static async Task<DriveItem?> ReadItemAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return ReadItem(document.RootElement);
    }

    private static DriveItem ReadItem(JsonElement element) =>
        new(
            element.GetProperty("id").GetString() ?? throw new InvalidDataException("Google Drive item has no id."),
            element.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("mimeType", out var type) ? type.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out var parsedSize) ? parsedSize : 0,
            element.TryGetProperty("modifiedTime", out var modified)
            && DateTimeOffset.TryParse(modified.GetString(), out var parsedModified)
                ? parsedModified
                : null,
            element.TryGetProperty("etag", out var etag) ? etag.GetString() : null
        );

    private static string[] Segments(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Uri.UnescapeDataString).ToArray();

    private sealed record DriveItem(string Id, string Name, string MimeType, long Size, DateTimeOffset? ModifiedUtc, string? ETag);
}

/// <summary>OneDrive transport confined to the OAuth application's special approot folder.</summary>
public sealed class OneDriveProvider : OAuthCloudProvider
{
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private readonly ISettingsManager _settings;

    public OneDriveProvider(IHttpClientFactory clients, ISettingsManager settings, ISecureCredentialStore credentials)
        : base(clients, settings, credentials) => _settings = settings;

    public override CloudProviderType ProviderType => CloudProviderType.OneDrive;
    protected override string CredentialKey => "cloud-onedrive";
    protected override Setting ClientIdSetting => _settings.CLOUD_ONEDRIVE_CLIENT_ID;
    protected override string AuthorizationEndpoint => "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    protected override string TokenEndpoint => "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    protected override IReadOnlyList<string> Scopes => ["offline_access", "Files.ReadWrite.AppFolder"];

    public override async Task<RemoteFileInfo?> GetFileInfoAsync(string path)
    {
        var item = await FindItemAsync(path);
        return item is null ? null : new RemoteFileInfo(path, item.Size, item.ModifiedUtc, item.ETag);
    }

    public override async Task DownloadAsync(string remotePath, string localPath)
    {
        var item =
            await FindItemAsync(remotePath) ?? throw new FileNotFoundException("The OneDrive cloud file does not exist.", remotePath);
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Graph}/me/drive/items/{item.Id}/content"));
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await using var output = File.Create(localPath);
        await response.Content.CopyToAsync(output);
    }

    public override async Task UploadAsync(string localPath, string remotePath)
    {
        var segments = Segments(remotePath);
        if (segments.Length == 0)
            throw new InvalidOperationException("A OneDrive cloud file path is required.");
        var parent = await EnsureFoldersAsync(segments[..^1]);
        var existing = await FindChildAsync(parent, segments[^1]);
        var info = new FileInfo(localPath);
        if (info.Length <= 4 * 1024 * 1024)
        {
            await using var stream = File.OpenRead(localPath);
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                $"{Graph}/me/drive/items/{parent}:/{EscapeSegment(segments[^1])}:/content"
            )
            {
                Content = new StreamContent(stream),
            };
            using var response = await SendAsync(request);
            response.EnsureSuccessStatusCode();
            return;
        }

        using var start = new HttpRequestMessage(
            HttpMethod.Post,
            existing is null
                ? $"{Graph}/me/drive/items/{parent}:/{EscapeSegment(segments[^1])}:/createUploadSession"
                : $"{Graph}/me/drive/items/{existing.Id}/createUploadSession"
        )
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        using var session = await SendAsync(start);
        session.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await session.Content.ReadAsStreamAsync());
        var uploadUrl =
            document.RootElement.GetProperty("uploadUrl").GetString()
            ?? throw new InvalidDataException("OneDrive did not create an upload session.");
        await using var input = File.OpenRead(localPath);
        await UploadChunksAsync(new Uri(uploadUrl), input, input.Length);
    }

    public override async Task<IReadOnlyList<string>> ListAsync(string path)
    {
        var folder = await FindItemAsync(path);
        if (folder is null)
            return [];
        var children = await ListChildrenAsync(folder.Id);
        var prefix = "/" + string.Join('/', Segments(path));
        return children.Select(child => $"{prefix}/{Uri.EscapeDataString(child.Name)}").ToList();
    }

    private async Task UploadChunksAsync(Uri uploadUri, Stream input, long length)
    {
        const int chunkSize = 320 * 1024;
        var buffer = new byte[chunkSize];
        long position = 0;
        while (position < length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(chunkSize, length - position)));
            if (read == 0)
                throw new EndOfStreamException("The local upload file ended unexpectedly.");
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri) { Content = new ByteArrayContent(buffer, 0, read) };
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(position, position + read - 1, length);
            using var response = await Client.SendAsync(request);
            if (position + read < length && response.StatusCode != HttpStatusCode.Accepted)
                response.EnsureSuccessStatusCode();
            if (position + read == length)
                response.EnsureSuccessStatusCode();
            position += read;
        }
    }

    private async Task<OneDriveItem?> FindItemAsync(string path)
    {
        var segments = Segments(path);
        if (segments.Length == 0)
            return await GetRootAsync();
        var itemPath = string.Join('/', segments.Select(EscapeSegment));
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"{Graph}/me/drive/special/approot:/{itemPath}:?$select=id,name,size,lastModifiedDateTime,eTag,folder"
            )
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await ReadItemAsync(response);
    }

    private async Task<OneDriveItem> GetRootAsync()
    {
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"{Graph}/me/drive/special/approot?$select=id,name,size,lastModifiedDateTime,eTag,folder"
            )
        );
        response.EnsureSuccessStatusCode();
        return await ReadItemAsync(response) ?? throw new InvalidDataException("OneDrive app folder has no id.");
    }

    private async Task<string> EnsureFoldersAsync(IReadOnlyList<string> segments)
    {
        var parent = (await GetRootAsync()).Id;
        foreach (var segment in segments)
        {
            var child = await FindChildAsync(parent, segment);
            if (child is not null)
            {
                if (!child.IsFolder)
                    throw new IOException($"OneDrive item '{segment}' is a file, not a folder.");
                parent = child.Id;
                continue;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Graph}/me/drive/items/{parent}/children")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        new Dictionary<string, object>
                        {
                            ["name"] = segment,
                            ["folder"] = new Dictionary<string, object>(),
                            ["@microsoft.graph.conflictBehavior"] = "fail",
                        }
                    ),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            using var response = await SendAsync(request);
            response.EnsureSuccessStatusCode();
            parent = (await ReadItemAsync(response))?.Id ?? throw new InvalidDataException("OneDrive did not create a folder.");
        }
        return parent;
    }

    private async Task<OneDriveItem?> FindChildAsync(string parentId, string name)
    {
        var children = await ListChildrenAsync(parentId);
        return children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<OneDriveItem>> ListChildrenAsync(string parentId)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"{Graph}/me/drive/items/{parentId}/children?$select=id,name,size,lastModifiedDateTime,eTag,folder&$top=999"
            )
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.TryGetProperty("value", out var values) ? values.EnumerateArray().Select(ReadItem).ToList() : [];
    }

    private static async Task<OneDriveItem?> ReadItemAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return ReadItem(document.RootElement);
    }

    private static OneDriveItem ReadItem(JsonElement element) =>
        new(
            element.GetProperty("id").GetString() ?? throw new InvalidDataException("OneDrive item has no id."),
            element.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
            element.TryGetProperty("lastModifiedDateTime", out var modified)
            && DateTimeOffset.TryParse(modified.GetString(), out var parsedModified)
                ? parsedModified
                : null,
            element.TryGetProperty("eTag", out var etag) ? etag.GetString() : null,
            element.TryGetProperty("folder", out _)
        );

    private static string EscapeSegment(string segment) => Uri.EscapeDataString(segment);

    private static string[] Segments(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Uri.UnescapeDataString).ToArray();

    private sealed record OneDriveItem(string Id, string Name, long Size, DateTimeOffset? ModifiedUtc, string? ETag, bool IsFolder);
}
