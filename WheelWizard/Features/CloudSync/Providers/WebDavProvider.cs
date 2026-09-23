using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using WheelWizard.CloudSync.Credentials;
using WheelWizard.Settings;

namespace WheelWizard.CloudSync.Providers;

/// <summary>WebDAV/Nextcloud transport. The URL is non-sensitive; the Basic/app password is a secret.</summary>
public class WebDavProvider(IHttpClientFactory clients, ISettingsManager settings, ISecureCredentialStore credentials) : ICloudProvider
{
    public virtual CloudProviderType ProviderType => CloudProviderType.WebDav;
    protected virtual string CredentialKey => "cloud-webdav";

    public virtual async Task AuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteRoot))
            throw new InvalidOperationException("Set the WebDAV remote root before connecting.");
        if (await credentials.GetAsync(CredentialKey) is null)
            throw new InvalidOperationException("No WebDAV credential is available in the secure credential store.");

        await ValidateConnectionAsync();
    }

    public virtual async Task<bool> IsAuthenticatedAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteRoot) || await credentials.GetAsync(CredentialKey) is null)
            return false;

        try
        {
            await ValidateConnectionAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ValidateConnectionAsync()
    {
        using var request = await CreateRequestAsync(HttpMethod.Options, string.Empty);
        using var response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new HttpRequestException($"WebDAV authentication failed ({(int)response.StatusCode}).");
    }

    public Task DisconnectAsync() => credentials.DeleteAsync(CredentialKey);

    public async Task<RemoteFileInfo?> GetFileInfoAsync(string path)
    {
        using var request = await CreateRequestAsync(HttpMethod.Head, path);
        using var response = await Client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return new RemoteFileInfo(
            path,
            response.Content.Headers.ContentLength ?? 0,
            response.Content.Headers.LastModified,
            response.Headers.ETag?.Tag
        );
    }

    public async Task DownloadAsync(string remotePath, string localPath)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, remotePath);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await using var output = File.Create(localPath);
        await response.Content.CopyToAsync(output);
    }

    public async Task UploadAsync(string localPath, string remotePath)
    {
        await EnsureParentCollectionsAsync(remotePath);
        await using var input = File.OpenRead(localPath);
        using var content = new StreamContent(input);
        using var request = await CreateRequestAsync(HttpMethod.Put, remotePath);
        request.Content = content;
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> ExistsAsync(string path) => await GetFileInfoAsync(path) is not null;

    public async Task<IReadOnlyList<string>> ListAsync(string path)
    {
        using var request = await CreateRequestAsync(new HttpMethod("PROPFIND"), path);
        request.Headers.TryAddWithoutValidation("Depth", "1");
        request.Content = new StringContent(
            "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>",
            Encoding.UTF8,
            "application/xml"
        );
        using var response = await Client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();

        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace dav = "DAV:";
        return document
            .Descendants(dav + "response")
            .Select(node => node.Element(dav + "href")?.Value)
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => href!)
            .ToList();
    }

    private async Task EnsureParentCollectionsAsync(string remotePath)
    {
        var segments = remotePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return;
        var current = string.Empty;
        foreach (var segment in segments[..^1])
        {
            current += "/" + segment;
            using var request = await CreateRequestAsync(new HttpMethod("MKCOL"), current);
            using var response = await Client.SendAsync(request);
            // 201 = created, 405 = already exists. Servers may return 301/204 for their
            // configured root; any other response is a real upload precondition failure.
            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NoContent))
                response.EnsureSuccessStatusCode();
        }
    }

    protected HttpClient Client => clients.CreateClient("WheelWizard.CloudSync.WebDav");
    protected ISettingsManager Settings => settings;
    protected ISecureCredentialStore Credentials => credentials;
    protected string RemoteRoot => settings.Get<string>(settings.CLOUD_REMOTE_ROOT).Trim();

    protected async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path)
    {
        if (!Uri.TryCreate(RemoteRoot.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute, out var uri))
            throw new InvalidOperationException("The WebDAV remote root is not a valid absolute URI.");
        var request = new HttpRequestMessage(method, uri);
        var secret = await credentials.GetAsync(CredentialKey);
        if (secret is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(secret.Value))
            );
        return request;
    }
}
