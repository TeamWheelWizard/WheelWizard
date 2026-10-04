using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.ApplicationData;

public class ApplicationDataDirectoriesTests
{
    [Theory]
    [InlineData(false, false, "/appdata/CT-MKWII")]
    [InlineData(true, false, "/launcher/CT-MKWII")]
    [InlineData(true, true, "/appdata/CT-MKWII")]
    public void DefaultLocation_HonorsPortableMarker_ExceptInsideFlatpak(bool portable, bool sandboxed, string expected)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux).UseCurrentDirectory("/launcher"));
        fs.Directory.CreateDirectory("/launcher");
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(true);
        environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns("/appdata");
        if (portable)
            fs.File.WriteAllText("portable-ww.txt", "");
        if (sandboxed)
        {
            fs.File.WriteAllText("/.flatpak-info", "");
            environment.GetEnvironmentVariable("FLATPAK_ID").Returns("org.example.WheelWizard");
        }

        Assert.Equal(expected, new ApplicationDataDirectories(fs, environment).DefaultDirectoryPath);
    }

    [Fact]
    public void FileLocationStore_RoundTripsAndClearsExistingMarkerFormat()
    {
        var fs = new MockFileSystem();
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns(fs.Path.GetFullPath("/appdata"));
        var directories = new ApplicationDataDirectories(fs, environment);
        var store = new FileApplicationDataLocationStore(fs, directories);

        Assert.Null(store.Load());
        store.Save("/custom/data");
        Assert.Equal("/custom/data", store.Load());
        Assert.Equal("/custom/data", fs.File.ReadAllText(directories.OverrideFilePath));
        store.Save(null);
        Assert.Null(store.Load());
    }
}
