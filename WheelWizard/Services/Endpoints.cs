namespace WheelWizard.Services;

public static class Endpoints
{
    /// <summary>
    /// The base address for accessing room data
    /// </summary>
    public const string RwfcBaseAddress = "https://rwfc.net";

    /// <summary>
    /// The base address for accessing the WheelWizard data (data that we control)
    /// </summary>
    public const string WhWzDataBaseAddress = "https://raw.githubusercontent.com/TeamWheelWizard/WheelWizard-Data/main";

    /// <summary>
    /// The base address for accessing the GameBanana API
    /// </summary>
    public const string GameBananaBaseAddress = "https://gamebanana.com/apiv11";

    /// <summary>
    /// The address for the GitHub API
    /// </summary>
    public const string GitHubAddress = "https://api.github.com";

    /// <summary>
    /// The address for the Mii image
    /// </summary>
    public const string MiiImageAddress = "https://studio.mii.nintendo.com";

    // TODO: Refactor all the URLs seen below

    // Retro Rewind
    public const string OldRRUrl = "http://update.rwfc.net:8000/";
    public const string RRUrl = "https://rwfc.net/updates/";
    public const string RRZipUrl = RRUrl + "RetroRewind/zip/RetroRewind.zip";
    public const string RRVersionUrl = RRUrl + "RetroRewind/RetroRewindVersion.txt";
    public const string RRVersionDeleteUrl = RRUrl + "RetroRewind/RetroRewindDelete.txt";
    public const string RRDiscordUrl = "https://discord.gg/yH3ReN8EhQ";

    // Branding Urls
    public const string WhWzDiscordUrl = "https://discord.gg/vZ7T2wJnsq";
    public const string WhWzGithubUrl = "https://github.com/TeamWheelWizard/WheelWizard";
    public const string SupportLink = "https://ko-fi.com/wheelwizard";

    // Other
    public const string MiiImageUrl = "https://studio.mii.nintendo.com/miis/image.png";
    public const string MiiChannelWAD = "-";

    //GameBanana
    public const string GameBananaBaseUrl = "https://gamebanana.com/apiv11";
}
