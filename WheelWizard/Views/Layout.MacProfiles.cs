using Avalonia.Controls;
using WheelWizard.Models.Enums;
using WheelWizard.Views.Pages;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views;

public partial class Layout
{
    internal void PopulateMacProfiles(NativeMenu menu)
    {
        menu.Items.Clear();
        var profiles = GameLicenseService.GetRegionLicenses();
        var activeRegion = SettingsService.Get<MarioKartWiiEnums.Regions>(SettingsService.RR_REGION);
        var activeSlot = SettingsService.Get<int>(SettingsService.FOCUSED_USER);
        foreach (
            var (region, name) in new[]
            {
                (MarioKartWiiEnums.Regions.Europe, "Europe"),
                (MarioKartWiiEnums.Regions.America, "America"),
                (MarioKartWiiEnums.Regions.Japan, "Japan"),
                (MarioKartWiiEnums.Regions.Korea, "Korea"),
            }
        )
        {
            if (menu.Items.Count > 0)
                menu.Add(new NativeMenuItemSeparator());
            menu.Add(new NativeMenuItem(name) { IsEnabled = false });
            foreach (var profile in profiles.Where(p => p.Region == region))
            {
                var label = string.IsNullOrWhiteSpace(profile.Name) ? $"Profile {profile.Slot + 1}" : profile.Name;
                var item = new NativeMenuItem($"{label} ({profile.FriendCode})")
                {
                    IsEnabled = CompleteContentEnabled,
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = activeRegion == region && activeSlot == profile.Slot,
                };
                item.Click += (_, _) => SelectMacProfile(profile);
                menu.Add(item);
            }
        }
    }

    private void SelectMacProfile(RegionLicense profile)
    {
        if (
            !CompleteContentEnabled
            || !GameLicenseService.GetRegionLicenses().Any(p => p.Region == profile.Region && p.Slot == profile.Slot)
        )
            return;
        SettingsService.Set(SettingsService.RR_REGION, profile.Region);
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
