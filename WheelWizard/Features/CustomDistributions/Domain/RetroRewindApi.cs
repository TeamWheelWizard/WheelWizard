using Refit;

namespace WheelWizard.CustomDistributions.Domain;

public interface IRetroRewindApi
{
    [Get("/RetroRewind/RetroRewind.zip")]
    Task<HttpContent> DownloadRetroRewindZip();

    [Get("/RetroRewind/RetroRewindVersion.txt")]
    Task<string> GetVersionFile();

    [Get("/RetroRewind/RetroRewindDelete.txt")]
    Task<string> GetDeletionFile();

    [Get("/")]
    Task<string> Ping(); // use to test server reachability
}
