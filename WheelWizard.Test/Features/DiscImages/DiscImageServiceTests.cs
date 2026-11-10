using System.Text;
using Testably.Abstractions.Testing;
using WheelWizard.DiscImages;
using WheelWizard.DiscImages.Domain;

namespace WheelWizard.Test.Features.DiscImages;

public class DiscImageServiceTests
{
    [Fact]
    public void ReadInfo_ReadsWiiDiscHeader()
    {
        var fs = Games();
        fs.File.WriteAllBytes("/games/game.iso", WiiDisc("RMCP01", "MarioKartWii"));

        var info = new DiscImageService(fs).ReadInfo("/games/game.iso");

        Assert.True(info.IsSuccess, info.IsFailure ? info.Error.Message : null);
        Assert.Equal(new DiscImageInfo("RMCP01", "MarioKartWii", 0, DiscImageFormat.Iso), info.Value);
    }

    [Fact]
    public void ReadInfo_RejectsFilesWithoutDiscHeader()
    {
        var fs = Games();
        fs.File.WriteAllBytes("/games/notes.iso", new byte[0x8000]);

        Assert.True(new DiscImageService(fs).ReadInfo("/games/notes.iso").IsFailure);
    }

    [Fact]
    public void ReadInfo_FailsForMissingFile()
    {
        Assert.True(new DiscImageService(new MockFileSystem()).ReadInfo("/missing.rvz").IsFailure);
    }

    [Fact]
    public void ReadInfo_RereadsChangedFile()
    {
        var fs = Games();
        var service = new DiscImageService(fs);
        fs.File.WriteAllBytes("/games/game.iso", WiiDisc("RMCE01", "MarioKartWii"));
        Assert.Equal("RMCE01", service.ReadInfo("/games/game.iso").Value.GameId);

        fs.File.WriteAllBytes("/games/game.iso", WiiDisc("RMCJ01", "MarioKartWii", extraBytes: 1));

        Assert.Equal("RMCJ01", service.ReadInfo("/games/game.iso").Value.GameId);
    }

    [Fact]
    public void ReadInfo_RetriesAfterFileWasLocked()
    {
        var fs = Games();
        var service = new DiscImageService(fs);
        fs.File.WriteAllBytes("/games/game.iso", WiiDisc("RMCP01", "MarioKartWii"));

        using (fs.File.Open("/games/game.iso", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.True(service.ReadInfo("/games/game.iso").IsFailure);

        Assert.Equal("RMCP01", service.ReadInfo("/games/game.iso").Value.GameId);
    }

    [Fact]
    public void SupportedExtensions_CoverWiiContainers()
    {
        var extensions = new DiscImageService(new MockFileSystem()).SupportedExtensions;

        Assert.Equal(new[] { ".iso", ".gcm", ".gcz", ".ciso", ".wbfs", ".wia", ".rvz" }.Order(), extensions.Order());
    }

    // A file can't be written to a drive root the mock hasn't seen yet, which the Windows CI runner hits on D:.
    private static MockFileSystem Games()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/games");
        return fs;
    }

    private static byte[] WiiDisc(string gameId, string name, int extraBytes = 0)
    {
        var data = new byte[0x50000 + extraBytes];
        Encoding.ASCII.GetBytes(gameId).CopyTo(data, 0);
        new byte[] { 0x5D, 0x1C, 0x9E, 0xA3 }.CopyTo(data, 0x18);
        Encoding.ASCII.GetBytes(name).CopyTo(data, 0x20);
        return data;
    }
}
