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
    public const string RrUrl = "http://update.rwfc.net:8000/";
    public const string RrZipUrl = RrUrl + "RetroRewind/zip/RetroRewind.zip";
    public const string RrVersionUrl = RrUrl + "RetroRewind/RetroRewindVersion.txt";
    public const string RrVersionDeleteUrl = RrUrl + "RetroRewind/RetroRewindDelete.txt";
    public const string RrDiscordUrl = "https://discord.gg/yH3ReN8EhQ";

    // Branding Urls
    public const string WhWzDiscordUrl = "https://discord.gg/vZ7T2wJnsq";
    public const string WhWzGithubUrl = "https://github.com/TeamWheelWizard/WheelWizard";
    public const string SupportLink = "https://ko-fi.com/wheelwizard";

    // Other
    public const string MiiStudioUrl = "https://qrcode.rc24.xyz/cgi-bin/studio.cgi";
    public const string MiiImageUrl = "https://studio.mii.nintendo.com/miis/image.png";
    public const string MiiChannelWad = "-";

    //GameBanana
    public const string GameBananaBaseUrl = "https://gamebanana.com/apiv11";
}
