namespace WheelWizard.Services;

public static class Endpoints
{
    /// <summary>
    /// The base address for accessing room data
    /// </summary>
    public const string RwfcBaseAddress = "http://rwfc.net";

    /// <summary>
    /// The base address for accessing the WheelWizard data (data that we control)
    /// </summary>
    public const string WhWzDataBaseAddress = "https://raw.githubusercontent.com/TeamWheelWizard/WheelWizard-Data/main";
    
    // TODO: Refactor all the URLs seen below

    // Retro Rewind
    public const string RRUrl = "http://update.rwfc.net:8000/";
    public const string RRZipUrl = RRUrl + "RetroRewind/zip/RetroRewind.zip";
    public const string RRVersionUrl = RRUrl + "RetroRewind/RetroRewindVersion.txt";
    public const string RRVersionDeleteUrl = RRUrl + "RetroRewind/RetroRewindDelete.txt";
    public const string RRDiscordUrl = "https://discord.gg/yH3ReN8EhQ";

    public const string WhWzLatestReleasesUrl = "https://api.github.com/repos/TeamWheelWizard/WheelWizard/releases/latest";

    //search depth 3 releases
    public const string WhWzReleasesUrl = "https://api.github.com/repos/TeamWheelWizard/WheelWizard/releases?per_page=3";

    // Branding Urls
    public const string WhWzDiscordUrl = "https://discord.gg/vZ7T2wJnsq";
    public const string WhWzGithubUrl = "https://github.com/TeamWheelWizard/WheelWizard";
    public const string SupportLink = "https://ko-fi.com/wheelwizard";

    // Other
    public const string MiiStudioUrl = "https://qrcode.rc24.xyz/cgi-bin/studio.cgi";
    public const string MiiImageUrl = "https://studio.mii.nintendo.com/miis/image.png";
    public const string MiiChannelWAD = "-";

    //GameBanana
    public const string GameBananaBaseUrl = "https://gamebanana.com/apiv11";
}
