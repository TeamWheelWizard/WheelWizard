using System.Net.Http;
using Refit;
using WheelWizard.Services;

namespace WheelWizard.MiiRendering.Domain;

public interface IMiiRenderingAssetApi
{
    [Get(Endpoints.MiiRenderingArchivePath)]
    Task<HttpResponseMessage> DownloadArchiveAsync(CancellationToken cancellationToken = default);
}
