using System.IO.Abstractions;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.MiiImages.Views;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Binary;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Couch.Input;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Views;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.UI.Test;

public class CouchHomeTests
{
    [AvaloniaFact]
    public async Task PrimaryLicenseChanges_UpdateTheExistingHomeAndControllerOpensThatProfile()
    {
        using var fixture = new HomeFixture();
        fixture.WriteSave(0, 3);
        fixture.Settings.FOCUSED_USER.Set(3, skipSave: true);
        fixture.Show();
        var home = Assert.IsType<HomePage>(fixture.Navigation.CurrentPage);
        await home.InitialContentReady;
        fixture.Pump();
        Assert.Equal("Player 4", fixture.Name.Text);
        Assert.Equal("Player 4", fixture.Mii.Mii!.Name.ToString());

        fixture.Settings.FOCUSED_USER.Set(0, skipSave: true);
        fixture.Pump();
        Assert.Same(home, fixture.Navigation.CurrentPage);
        Assert.Equal("Player 1", fixture.Name.Text);
        Assert.Equal("Player 1", fixture.Mii.Mii!.Name.ToString());

        var play = home.FindControl<Button>("CouchPlayButton")!;
        Assert.Same(play, home.CouchFocusTarget);
        play.Focus(NavigationMethod.Directional);
        Assert.True(CouchInputController.MoveFocus(fixture.Window, play, NavigationDirection.Left));
        Assert.Same(fixture.Profile, fixture.Window.FocusManager!.GetFocusedElement());
        CouchNativeInput.SendKey(fixture.Profile, Key.Space);
        fixture.Pump();
        var profile = Assert.IsType<UserProfilePage>(fixture.Navigation.CurrentPage);
        Assert.Equal("Player 1", profile.CurrentMii!.Name.ToString());
    }

    [AvaloniaFact]
    public async Task EmptyPrimarySlot_DoesNotBorrowAnotherLicense_AndPollingClearsRemovedSave()
    {
        using var fixture = new HomeFixture();
        fixture.WriteSave(0);
        fixture.Settings.FOCUSED_USER.Set(2, skipSave: true);
        fixture.Show();
        Assert.False(fixture.Profile.IsEnabled);
        Assert.Null(fixture.Mii.Mii);

        fixture.WriteSave(0, 2);
        await fixture.PollLicenses();
        Assert.True(fixture.Profile.IsEnabled);
        Assert.Equal("Player 3", fixture.Name.Text);
        Assert.Equal("Player 3", fixture.Mii.Mii!.Name.ToString());
        var home = Assert.IsType<HomePage>(fixture.Navigation.CurrentPage);
        fixture.Window.TogglePresentation();
        fixture.Pump();
        Assert.Same(home, fixture.Navigation.CurrentPage);
        Assert.Null(fixture.Mii.Mii);
        fixture.Window.TogglePresentation();
        fixture.Pump();
        Assert.Equal("Player 3", fixture.Mii.Mii!.Name.ToString());

        fixture.FileSystem.File.Delete(fixture.SavePath);
        await fixture.PollLicenses();
        Assert.False(fixture.Profile.IsEnabled);
        Assert.Null(fixture.Mii.Mii);
        Assert.DoesNotContain("Player", fixture.Name.Text!);
        Assert.Same(home, fixture.Navigation.CurrentPage);
    }

    [AvaloniaFact]
    public async Task DeckResolutionAt150Percent_KeepsPrimaryLicenseAndLaunchActionInsideTheViewport()
    {
        using var fixture = new HomeFixture();
        fixture.WriteSave(0);
        fixture.Show();
        var home = Assert.IsType<HomePage>(fixture.Navigation.CurrentPage);
        await home.InitialContentReady;
        fixture.Window.Height = 720;
        fixture.Settings.WINDOW_SCALE.Set(1.5, skipSave: true);
        fixture.Pump();
        var viewport = home.FindControl<ScrollViewer>("CouchHome")!;
        foreach (var control in new Control[] { fixture.Profile, fixture.Name, home.FindControl<Button>("CouchPlayButton")! })
        {
            var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(viewport)!.Value);
            Assert.True(new Rect(viewport.Bounds.Size).Contains(bounds), $"{control.Name} is clipped: {bounds} in {viewport.Bounds.Size}.");
        }
    }

    private sealed class HomeFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public IFileSystem FileSystem { get; } = new MockFileSystem();
        public ISettingsManager Settings { get; }
        public Layout Window { get; }
        public INavigationService Navigation { get; }
        public string SavePath { get; }
        private HomePage Home => (HomePage)Navigation.CurrentPage!;
        public Button Profile => Home.FindControl<Button>("CouchProfileButton")!;
        public TextBlock Name => Home.FindControl<TextBlock>("CouchPrimaryName")!;
        public MiiAnimatedImage Mii => Home.FindControl<MiiAnimatedImage>("CouchPrimaryMii")!;

        public HomeFixture()
        {
            var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-home-tests");
            FileSystem.Directory.CreateDirectory(directory);
            SavePath = Path.Combine(directory, "saves", "RMCP", "rksys.dat");
            var location = Substitute.For<IApplicationDataLocation>();
            location.DirectoryPath.Returns(directory);
            var paths = Substitute.For<ICustomDistributionPaths>();
            paths.SaveFolderPath.Returns(Path.Combine(directory, "saves"));
            var regions = Substitute.For<ISaveRegionService>();
            regions.ResolveActiveRegion(Arg.Any<string>()).Returns(MarioKartWiiEnums.Regions.Europe);
            var miis = Substitute.For<IMiiDbService>();
            miis.GetByAvatarId(Arg.Any<uint>())
                .Returns(call =>
                {
                    var mii = MiiFactory.CreateDefaultMale();
                    mii.Name = new MiiName($"Player {call.Arg<uint>()}");
                    return OperationResult<Mii>.Ok(mii);
                });
            var registrations = new ServiceCollection();
            registrations.AddWheelWizardServices(location);
            registrations.AddSingleton(FileSystem);
            registrations.AddSingleton(paths);
            registrations.AddSingleton(regions);
            registrations.AddSingleton(miis);
            registrations.AddTransient(typeof(IApiCaller<>), typeof(ApplicationCompositionTests.OfflineApiCaller<>));
            _services = registrations.BuildServiceProvider();
            _services.GetRequiredService<ISettingsStartupInitializer>().Initialize();
            Settings = _services.GetRequiredService<ISettingsManager>();
            Settings.ENABLE_ANIMATIONS.Set(false, skipSave: true);
            Settings.NAND_ROOT_PATH.Set(directory, skipSave: true);
            Settings.LOAD_PATH.Set(directory, skipSave: true);
            _services.GetRequiredService<MiiControlThemes>().Install(Application.Current!.Resources);
            _services.GetRequiredService<WindowAppearance>().Install(Application.Current.Resources);
            Navigation = _services.GetRequiredService<INavigationService>();
            Window = _services.GetRequiredService<Layout>();
        }

        public void WriteSave(params int[] slots)
        {
            var data = new byte[0x2BC000];
            Encoding.ASCII.GetBytes("RKSD0006").CopyTo(data, 0);
            foreach (var slot in slots)
            {
                var offset = 8 + slot * 0x8CC0;
                Encoding.ASCII.GetBytes("RKPD").CopyTo(data, offset);
                BigEndianBinary.WriteUInt32BigEndian(data, offset + 0x28, (uint)(slot + 1));
                BigEndianBinary.WriteUInt32BigEndian(data, offset + 0x5C, (uint)(slot + 1));
            }
            FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(SavePath)!);
            FileSystem.File.WriteAllBytes(SavePath, data);
        }

        public void Show()
        {
            Window.Show();
            Pump();
            Window.TogglePresentation();
            Window.Width = 1280;
            Window.Height = 800;
            Pump();
        }

        public void Pump()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public async Task PollLicenses()
        {
            var licenses = (GameLicenseSingletonService)_services.GetRequiredService<IGameLicenseSingletonService>();
            licenses.Start();
            await licenses.InitialUpdate;
            licenses.Stop();
            Pump();
        }

        public void Dispose()
        {
            Window.Close();
            _services.Dispose();
        }
    }
}
