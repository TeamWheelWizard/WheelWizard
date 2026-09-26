using System.Globalization;

namespace WheelWizard.GameBanana.InstallRequests;

public sealed record ModInstallRequest(int ModId, string? DownloadUrl)
{
    public static OperationResult<ModInstallRequest> Parse(string url)
    {
        const string prefix = "wheelwizard://";
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Fail("Expected a wheelwizard:// install URL.");
        var content = url[prefix.Length..].Trim();
        var separator = content.IndexOf(',');
        var idText = (separator < 0 ? content : content[..separator]).Trim().TrimEnd('/');
        if (!int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var modId) || modId <= 0)
            return Fail($"Invalid ModID: {idText}");
        var downloadUrl = separator < 0 ? null : content[(separator + 1)..].Trim();
        if (string.IsNullOrEmpty(downloadUrl))
            downloadUrl = null;
        if (
            downloadUrl != null
            && (
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri)
                || (downloadUri.Scheme != Uri.UriSchemeHttps && downloadUri.Scheme != Uri.UriSchemeHttp)
            )
        )
            return Fail("The download URL must be an absolute HTTP or HTTPS URL.");
        return new ModInstallRequest(modId, downloadUrl);
    }
}
