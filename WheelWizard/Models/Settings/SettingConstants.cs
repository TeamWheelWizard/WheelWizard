namespace WheelWizard.Models.Settings;

public enum DolphinShaderCompilationMode
{
    Default = 0,
    ExclusiveUberShaders = 1,
    HybridUberShaders = 2,
    SkipDrawing = 3,
}

public static class SettingValues
{
    // These should not be seen, but are instead a placeholder for values. When you then use them to display something
    // you check for this value and replace it with its corresponding value in the language file
    public const string NoName = "no name";
    public const string NoLicense = "no license";

    public static readonly double[] WindowScales = [0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.8, 2];

    public static readonly Dictionary<string, string> GFXRenderers = new() //Display name, value
    {
#if WINDOWS
        { "DirectX 11 (Default)", "D3D11" },
        { "DirectX 12", "D3D12" },
#endif
        { "Vulkan", "Vulkan" },
# if MACOS
        { "Metal", "Metal" },
#endif
        { "OpenGL", "OGL" },
    };

    public static readonly Dictionary<string, Func<string>> WhWzLanguages = new()
    {
        { "en", () => CreateLanguageString("English") },
        { "nl", () => CreateLanguageString("Dutch") },
        { "fr", () => CreateLanguageString("France") },
        { "de", () => CreateLanguageString("German") },
        { "ja", () => CreateLanguageString("Japanese") },
        { "es", () => CreateLanguageString("Spanish") },
        { "it", () => CreateLanguageString("Italian") },
        { "ru", () => CreateLanguageString("Russian") },
        { "ko", () => CreateLanguageString("Korean") },
        { "tr", () => CreateLanguageString("Turkish") },
    };

    private static string CreateLanguageString(string language)
    {
        var lang = Resources.Languages.Settings.ResourceManager.GetString($"Value_Language_{language}")!;
        var langOg = Resources.Languages.Settings.ResourceManager.GetString($"Value_Language_{language}Og");
        if (lang == langOg || langOg == null || langOg == "-")
            return lang;

        return $"{lang} - ({langOg})";
    }
}
