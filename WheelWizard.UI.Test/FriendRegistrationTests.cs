using System.IO.Abstractions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Models.RRInfo;
using WheelWizard.RrRooms.Views;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WheelWizardData.Views.Dialogs;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.UI.Test;

public class FriendRegistrationTests
{
    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(false, 30)]
    [InlineData(true, 1)]
    [InlineData(true, 30)]
    public async Task AddFriend_OffersConfirmationForAnExistingFriendEvenWhenFull(bool fromRoom, int friendCount)
    {
        const string friendCode = "3484-8484-8484";
        var game = Substitute.For<IGameLicenseSingletonService>();
        var licenseCode = WheelWizard.WiiManagement.FriendCodes.FriendCode.ProfileIdToFriendCode(12345).ToString("D12");
        game.ActiveUser.Returns(
            new LicenseProfile
            {
                FriendCode = $"{licenseCode[..4]}-{licenseCode[4..8]}-{licenseCode[8..]}",
                Mii = null,
                Vr = 5000,
                Br = 5000,
                RegionId = 0,
                TotalRaceCount = 0,
                TotalWinCount = 0,
            }
        );
        var pending = new FriendProfile
        {
            FriendCode = friendCode,
            Mii = null,
            Vr = 5000,
            Br = 5000,
            RegionId = 0,
            Wins = 0,
            Losses = 0,
            CountryCode = 0,
            IsPending = true,
        };
        game.ActiveCurrentFriends.Returns(Enumerable.Repeat(pending, friendCount).ToList());
        var fs = new MockFileSystem();
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-friend-ui");
        fs.Directory.CreateDirectory(directory);
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(directory);
        var registrations = new ServiceCollection();
        registrations.AddWheelWizardServices(location);
        registrations.AddSingleton<IFileSystem>(fs);
        registrations.AddSingleton(game);
        registrations.AddTransient(typeof(IApiCaller<>), typeof(ApplicationCompositionTests.OfflineApiCaller<>));
        using var services = registrations.BuildServiceProvider();
        services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
        var settings = services.GetRequiredService<ISettingsManager>();
        settings.NAND_ROOT_PATH.Set(directory, skipSave: true);
        settings.LOAD_PATH.Set(directory, skipSave: true);
        var mii = new Mii { Name = new MiiName("Friend") };
        Control page;
        MenuItem add;
        if (fromRoom)
        {
            var player = new RrPlayer
            {
                Pid = "592497508",
                Name = "Friend",
                FriendCode = friendCode,
                Mii = mii,
            };
            var room = new RrRoom
            {
                Id = "room",
                Created = DateTime.UtcNow,
                Type = "private",
                Suspend = false,
                Players = [player],
            };
            var roomPage = ActivatorUtilities.CreateInstance<RoomDetailsPage>(services, room);
            var players = roomPage.FindControl<ListBox>("PlayersListView")!;
            add = players.ContextMenu!.Items.OfType<MenuItem>().First();
            Assert.Contains("friend", add.Header!.ToString()!, StringComparison.OrdinalIgnoreCase);
            players.ContextMenu = null;
            players.SelectedItem = player;
            page = roomPage;
        }
        else
        {
            page = ActivatorUtilities.CreateInstance<LeaderboardPage>(services);
            var menus = page.GetLogicalDescendants()
                .OfType<Control>()
                .Where(control => control.ContextMenu != null)
                .Select(control => control.ContextMenu!)
                .ToList();
            Assert.Equal(3, menus.Count);
            foreach (var menu in menus)
                Assert.Contains("friend", menu.Items.OfType<MenuItem>().First().Header!.ToString()!, StringComparison.OrdinalIgnoreCase);
            add = menus[0].Items.OfType<MenuItem>().First();
            add.DataContext = new LeaderboardPlayerItem
            {
                Rank = 1,
                PlacementLabel = "1",
                Name = "Friend",
                FriendCode = friendCode,
                VrText = "5000",
                Mii = mii,
            };
        }
        using var host = new UiTestHost();
        host.Show();
        add.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var confirmation = Assert.IsType<AddFriendConfirmationWindow>(host.Modals.TopModal);
        Assert.Equal(friendCode, confirmation.FriendCode);
        confirmation.Close();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        game.DidNotReceive().AddFriend(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Mii>(), Arg.Any<uint>(), Arg.Any<uint>());
    }
}
