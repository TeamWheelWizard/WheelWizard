#if WINDOWS
using Microsoft.Win32;
#endif
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.ModManagement;

namespace WheelWizard.Services.UrlProtocol;

public class UrlProtocolManager
{
    public const string ProtocolName = "wheelwizard";

    #region Windows only
    
#if WINDOWS
    public static void RegisterCustomScheme(string schemeName)
    {
        var currentExecutablePath = Process.GetCurrentProcess().MainModule!.FileName;
        var protocolKey = $@"SOFTWARE\Classes\{schemeName}";

        using var key = Registry.CurrentUser.CreateSubKey(protocolKey);

        key.SetValue("", $"URL:{schemeName} Protocol");
        key.SetValue("URL Protocol", "");


        using var shellKey = key.CreateSubKey(@"shell\open\command");
        shellKey.SetValue("", $"\"{currentExecutablePath}\" \"%1\"");
    }
    
    public static bool IsCustomSchemeRegistered(string schemeName)
    {
        var protocolKey = $@"SOFTWARE\Classes\{schemeName}";
        return Registry.CurrentUser.OpenSubKey(protocolKey) != null;
    }
        public static void RemoveCustomScheme(string schemeName)
    {
        var protocolKey = $@"SOFTWARE\Classes\{schemeName}";
        Registry.CurrentUser.DeleteSubKeyTree(protocolKey);
    }

    async private static void SetWhWzSchemeAsyncInternally()
    {
        var currentExecutablePath = Process.GetCurrentProcess().MainModule!.FileName;
        var protocolKey = $@"SOFTWARE\Classes\{ProtocolName}";

        // Check if the scheme is registered
        using var key = Registry.CurrentUser.OpenSubKey(protocolKey);
        if (key == null)
        {
            RegisterCustomScheme(ProtocolName);
            return;
        }
        
        using var shellKey = key.OpenSubKey(@"shell\open\command");
        if (shellKey != null)
        {
            var registeredExecutablePath = shellKey.GetValue("") as string;

            if (registeredExecutablePath != null)
            {
                // Extract the path from the registered string (which might have quotes and "%1")
                registeredExecutablePath = registeredExecutablePath.Split('\"')[1];

                // If the registered executable is different from the current one, repair it
                if (!registeredExecutablePath.Equals(currentExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Fix the scheme by re-registering with the current executable
                    RegisterCustomScheme(ProtocolName);
                }
            }
        }
    }
#endif
    
    public static async void SetWhWzSchemeAsync()
    {
#if WINDOWS
        SetWhWzSchemeAsyncInternally();
#endif
    }
    #endregion

    public static async Task ShowPopupForLaunchUrlAsync(string url)
    {
        // Remove the protocol prefix
        var content = url.Replace("wheelwizard://", "").Trim().TrimEnd('/');
        var parts = content.Split(',');
        try
        {
            if (!int.TryParse(parts[0], out var modID))
                throw new FormatException($"Invalid ModID: {parts[0]}");
            
            var downloadURL = parts.Length > 1 ? parts[1] : null;
            var modPopup = new ModIndependentWindow();
            await modPopup.LoadModAsync(modID, downloadURL);
            modPopup.ShowDialog();

        }
        catch (Exception ex)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Couldn't load URL")
                .SetInfoText($"Error handling URL: {ex.Message}")
                .Show();
        }
    }
}

