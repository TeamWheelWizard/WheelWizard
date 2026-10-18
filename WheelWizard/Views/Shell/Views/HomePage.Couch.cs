using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Polling;
using WheelWizard.Views.Shell.Couch;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views.Shell.Views;

public partial class HomePage
{
    private readonly IGameLicenseSingletonService _gameLicenses;
    private readonly ISettingsManager _settings;
    private readonly ISettingsSignalBus _settingsSignals;
    private readonly INavigationService _navigation;
    private IDisposable? _primarySettingsSubscription;
    private bool _primaryRefreshQueued;

    public Control? CouchFocusTarget => CouchPlayButton.IsEffectivelyEnabled ? CouchPlayButton : CouchProfileButton;

    public bool TryGoBack() => false;

    private void UpdatePrimaryLicenseSubscription()
    {
        if (!_isAttached || !CouchMode.GetIsEnabled(this))
        {
            _primarySettingsSubscription?.Dispose();
            _primarySettingsSubscription = null;
            _gameLicenses.Unsubscribe(this);
            CouchPrimaryMii.Mii = null;
            return;
        }

        if (_primarySettingsSubscription is not null)
            return;

        ReloadPrimaryLicense();
        _gameLicenses.Subscribe(this);
        _primarySettingsSubscription = _settingsSignals.Subscribe(OnPrimaryLicenseSettingChanged);
    }

    private void OnPrimaryLicenseSettingChanged(SettingChangedSignal signal)
    {
        var setting = signal.Setting;
        if (
            setting != _settings.FOCUSED_USER
            && setting != _settings.RR_REGION
            && setting != _settings.NAND_ROOT_PATH
            && setting != _settings.LOAD_PATH
            && setting != _settings.ENABLE_RECOMP
            && setting != _settings.RECOMP_NAND_ROOT
            && setting != _settings.WW_LANGUAGE
        )
            return;

        // Loading a different region can itself change the primary slot. Read the completed
        // save on the next UI turn rather than re-entering LoadLicense from its setting signal.
        if (_primaryRefreshQueued)
            return;
        _primaryRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _primaryRefreshQueued = false;
            if (_isAttached && CouchMode.GetIsEnabled(this))
                ReloadPrimaryLicense();
        });
    }

    private void ReloadPrimaryLicense()
    {
        _gameLicenses.LoadLicense();
        RefreshPrimaryLicense();
    }

    public void OnUpdate(ObservablePollingService sender) => RefreshPrimaryLicense();

    private void RefreshPrimaryLicense()
    {
        var slot = _settings.Get<int>(_settings.FOCUSED_USER);
        var users = _gameLicenses.LicenseCollection.Users;
        var user = slot >= 0 && slot < users.Count ? users[slot] : null;
        var mii = user?.Mii;
        var hasLicense = user is not null && user.NameOfMii != SettingValues.NoLicense;
        CouchProfileButton.IsEnabled = hasLicense;
        CouchPrimaryMii.Mii = hasLicense ? mii : null;
        CouchPrimaryName.Text =
            !hasLicense ? t("couch.no_primary_license")
            : string.IsNullOrWhiteSpace(user!.NameOfMii) || user.NameOfMii == SettingValues.NoName ? t("state.no_name")
            : user.NameOfMii;
    }

    private void CouchProfileButton_OnClick(object? sender, RoutedEventArgs e) => _navigation.NavigateTo<UserProfilePage>();
}
