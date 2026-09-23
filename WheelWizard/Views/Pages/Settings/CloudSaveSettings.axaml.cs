using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.CloudSync;
using WheelWizard.CloudSync.Credentials;
using WheelWizard.CloudSync.Providers;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;

namespace WheelWizard.Views.Pages.Settings;

public partial class CloudSaveSettings : UserControlBase
{
    private static readonly string[] ProviderItems = ["WebDav", "Nextcloud", "Google Drive", "OneDrive"];
    private bool _loading;
    private bool _signedIn;

    [Inject]
    private ISettingsManager Settings { get; set; } = null!;

    [Inject]
    private ICloudSyncService CloudSync { get; set; } = null!;

    [Inject]
    private ICloudProviderResolver Providers { get; set; } = null!;

    [Inject]
    private ISecureCredentialStore Credentials { get; set; } = null!;

    public CloudSaveSettings()
    {
        InitializeComponent();
        _loading = true;
        Enabled.IsChecked = Settings.Get<bool>(Settings.CLOUD_SYNC_ENABLED);
        BeforeLaunch.IsChecked = Settings.Get<bool>(Settings.SYNC_BEFORE_LAUNCH);
        AfterLaunch.IsChecked = Settings.Get<bool>(Settings.SYNC_AFTER_LAUNCH);
        Provider.SelectedIndex = Math.Max(0, Array.IndexOf(ProviderItems, Settings.Get<string>(Settings.CLOUD_PROVIDER_TYPE)));
        ConfigureProviderFields();
        _loading = false;
        _ = RefreshStatusAsync();
        _ = RefreshAuthorizationStateAsync();
    }

    private CloudProviderType SelectedProvider =>
        Provider.SelectedIndex is >= 0 and < 4
            ? Enum.Parse<CloudProviderType>(ProviderItems[Provider.SelectedIndex])
            : CloudProviderType.WebDav;

    private void Provider_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        ConfigureProviderFields();
        _ = RefreshAuthorizationStateAsync();
    }

    private void ConfigureProviderFields()
    {
        var provider = SelectedProvider;
        ServerSettings.IsVisible = provider is CloudProviderType.WebDav or CloudProviderType.Nextcloud;
        WebDavCredentials.IsVisible = provider == CloudProviderType.WebDav;
        OAuthClientSettings.IsVisible = provider is CloudProviderType.GoogleDrive or CloudProviderType.OneDrive;
        BrowserAuthorizationHint.IsVisible =
            provider is CloudProviderType.Nextcloud or CloudProviderType.GoogleDrive or CloudProviderType.OneDrive;
        ConnectButton.IsVisible = true;

        switch (provider)
        {
            case CloudProviderType.WebDav:
                ServerUrlLabel.Text = "WebDAV collection URL";
                ServerUrlHint.Text = "Enter the WebDAV collection URL, for example https://server.example/remote.php/dav/files/your-user.";
                RemoteRoot.PlaceholderText = "https://server.example/remote.php/dav/files/your-user";
                RemoteRoot.Text = Settings.Get<string>(Settings.CLOUD_REMOTE_ROOT);
                BrowserAuthorizationHint.Text = string.Empty;
                SetSignInButton(false);
                break;
            case CloudProviderType.Nextcloud:
                ServerUrlLabel.Text = "Nextcloud server URL";
                ServerUrlHint.Text = "Enter only the base server URL, for example https://cloud.example.com. Do not enter a WebDAV path.";
                RemoteRoot.PlaceholderText = "https://cloud.example.com";
                var savedServer = Settings.Get<string>(Settings.CLOUD_NEXTCLOUD_SERVER);
                var legacyRoot = Settings.Get<string>(Settings.CLOUD_REMOTE_ROOT);
                RemoteRoot.Text =
                    string.IsNullOrWhiteSpace(savedServer) && !legacyRoot.Contains("/remote.php/dav/", StringComparison.OrdinalIgnoreCase)
                        ? legacyRoot
                        : savedServer;
                BrowserAuthorizationHint.Text =
                    "Click Sign in. Nextcloud opens in your default browser, including any 2FA prompt. WheelWizard never asks for or stores your account password.";
                SetSignInButton(false);
                break;
            case CloudProviderType.GoogleDrive:
                OAuthClientIdLabel.Text = "Google OAuth client ID";
                OAuthClientIdHint.Text =
                    "Create a Desktop OAuth client in Google Cloud and paste its public client ID here. Desktop clients support the secure loopback return used by WheelWizard; no client secret is used.";
                OAuthClientId.Text = Settings.Get<string>(Settings.CLOUD_GOOGLE_CLIENT_ID);
                BrowserAuthorizationHint.Text =
                    "Click Sign in to authorize WheelWizard in your browser. The refresh token is stored only in your OS credential store and Drive access is limited to WheelWizard's app data folder.";
                SetSignInButton(false);
                break;
            case CloudProviderType.OneDrive:
                OAuthClientIdLabel.Text = "Microsoft application (client) ID";
                OAuthClientIdHint.Text =
                    "Create a public desktop client application in Microsoft Entra and paste its Application (client) ID here. Enable its loopback redirect for desktop apps; no client secret is used.";
                OAuthClientId.Text = Settings.Get<string>(Settings.CLOUD_ONEDRIVE_CLIENT_ID);
                BrowserAuthorizationHint.Text =
                    "Click Sign in to authorize WheelWizard in your browser. The refresh token is stored only in your OS credential store and files are kept in WheelWizard's OneDrive app folder.";
                SetSignInButton(false);
                break;
        }
    }

    private void SaveSettings()
    {
        var provider = SelectedProvider;
        Settings.Set(Settings.CLOUD_SYNC_ENABLED, Enabled.IsChecked == true);
        Settings.Set(Settings.SYNC_BEFORE_LAUNCH, BeforeLaunch.IsChecked == true);
        Settings.Set(Settings.SYNC_AFTER_LAUNCH, AfterLaunch.IsChecked == true);
        Settings.Set(Settings.CLOUD_PROVIDER_TYPE, provider.ToString());
        if (provider == CloudProviderType.WebDav)
            Settings.Set(Settings.CLOUD_REMOTE_ROOT, RemoteRoot.Text?.Trim() ?? string.Empty);
        else if (provider == CloudProviderType.Nextcloud)
            Settings.Set(Settings.CLOUD_NEXTCLOUD_SERVER, (RemoteRoot.Text ?? string.Empty).Trim().TrimEnd('/'));
        else if (provider == CloudProviderType.GoogleDrive)
            Settings.Set(Settings.CLOUD_GOOGLE_CLIENT_ID, OAuthClientId.Text?.Trim() ?? string.Empty);
        else if (provider == CloudProviderType.OneDrive)
            Settings.Set(Settings.CLOUD_ONEDRIVE_CLIENT_ID, OAuthClientId.Text?.Trim() ?? string.Empty);
    }

    private async void Connect_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        try
        {
            var provider = SelectedProvider;
            if (_signedIn)
            {
                await Providers.Resolve(provider).DisconnectAsync();
                SetSignInButton(false);
                Status.Text = "Signed out.";
                return;
            }
            if (provider == CloudProviderType.WebDav)
            {
                var hasUserName = !string.IsNullOrWhiteSpace(UserName.Text);
                var hasPassword = !string.IsNullOrWhiteSpace(AppPassword.Text);
                if (hasUserName != hasPassword)
                    throw new InvalidOperationException("Enter both the WebDAV user name and app password.");
                if (hasUserName)
                {
                    await Credentials.SaveAsync("cloud-webdav", new Secret($"{UserName.Text}:{AppPassword.Text}"));
                    AppPassword.Text = string.Empty;
                }
            }
            else if (provider == CloudProviderType.Nextcloud)
            {
                Status.Text =
                    "Opening Nextcloud in your browser. Log in there and grant WheelWizard access; this page will finish automatically.";
            }
            else
            {
                Status.Text = "Opening your browser. Complete the sign-in there; this page will finish automatically.";
            }

            await Providers.Resolve(provider).AuthenticateAsync();
            ConfigureProviderFields();
            SetSignInButton(true);
            Status.Text = provider switch
            {
                CloudProviderType.Nextcloud => "Nextcloud authorization succeeded.",
                CloudProviderType.GoogleDrive => "Google Drive authorization succeeded.",
                CloudProviderType.OneDrive => "OneDrive authorization succeeded.",
                _ => "WebDAV connection succeeded.",
            };
        }
        catch (Exception exception)
        {
            Status.Text = exception.Message;
        }
    }

    private async void SyncNow_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        var result = await CloudSync.SyncNowAsync();
        Status.Text = result.Message;
    }

    private async Task RefreshStatusAsync()
    {
        var status = await CloudSync.GetStatusAsync();
        Status.Text = status.Message;
    }

    private async Task RefreshAuthorizationStateAsync()
    {
        var provider = SelectedProvider;
        SetSignInButton(false);
        try
        {
            var signedIn = await Providers.Resolve(provider).IsAuthenticatedAsync();
            if (provider == SelectedProvider)
                SetSignInButton(signedIn);
        }
        catch
        {
            if (provider == SelectedProvider)
                SetSignInButton(false);
        }
    }

    private void SetSignInButton(bool signedIn)
    {
        _signedIn = signedIn;
        ConnectButton.Text = signedIn ? "Signed in" : "Sign in";
        ConnectButton.Variant = signedIn
            ? WheelWizard.Views.Components.Button.ButtonsVariantType.Primary
            : WheelWizard.Views.Components.Button.ButtonsVariantType.Default;
        ConnectButton.IconData = signedIn ? Icon("CheckMark") : null!;
    }

    private void ConnectButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_signedIn)
            return;

        ConnectButton.Text = "Sign out";
        ConnectButton.IconData = Icon("SignOut");
        ConnectButton.Variant = WheelWizard.Views.Components.Button.ButtonsVariantType.Danger;
    }

    private void ConnectButton_OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_signedIn)
            SetSignInButton(true);
    }

    private static Geometry Icon(string key) => (Geometry)Application.Current!.FindResource(key)!;
}
