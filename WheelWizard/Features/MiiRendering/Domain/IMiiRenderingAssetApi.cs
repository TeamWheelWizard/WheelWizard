using System.Net.Http;
using Refit;

namespace WheelWizard.MiiRendering.Domain;

public interface IMiiRenderingAssetApi
{
    [Get(MiiRenderingEndpoints.ResourceArchivePath)]
    Task<HttpResponseMessage> DownloadArchiveAsync(CancellationToken cancellationToken = default);
}
