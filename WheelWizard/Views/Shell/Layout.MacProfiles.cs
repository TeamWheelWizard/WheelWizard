using Avalonia.Controls;
using WheelWizard.Models.Enums;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views.Shell;

public partial class Layout
{
    internal void PopulateMacProfiles(NativeMenu menu)
    {
        menu.Items.Clear();
        var profiles = GameLicenseService.GetRegionLicenses();
        var activeRegion = SettingsService.Get<MarioKartWiiEnums.Regions>(SettingsService.RR_REGION);
        var activeSlot = SettingsService.Get<int>(SettingsService.FOCUSED_USER);
        // The region follows the game file, so only its profiles can be switched to.
        foreach (var profile in profiles.Where(p => p.Region == activeRegion))
        {
            var label = string.IsNullOrWhiteSpace(profile.Name) ? $"Profile {profile.Slot + 1}" : profile.Name;
            var item = new NativeMenuItem($"{label} ({profile.FriendCode})")
            {
                IsEnabled = CompleteContentEnabled,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = activeSlot == profile.Slot,
            };
            item.Click += (_, _) => SelectMacProfile(profile);
            menu.Add(item);
        }
    }

    private void SelectMacProfile(RegionLicense profile)
    {
        if (
            !CompleteContentEnabled
            || !GameLicenseService.GetRegionLicenses().Any(p => p.Region == profile.Region && p.Slot == profile.Slot)
        )
            return;
        SettingsService.Set(SettingsService.FOCUSED_USER, profile.Slot);
        GameLicenseService.LoadLicense();
        UpdateSidebarProfile();
        UpdateFriendCount();
        Activate();
        Navigation.NavigateTo<UserProfilePage>();
        if (Navigation.CurrentPage is UserProfilePage page)
            page.RefreshSelectedProfile();
    }
}
