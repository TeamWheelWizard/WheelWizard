using System.Buffers.Binary;
using Testably.Abstractions.Testing;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features;

public class NativeMiiResourceDependencyTests
{
    [Fact]
    public void Renderer_ReadsResourcesFromItsRegisteredFilesystem()
    {
        var fileSystem = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        fileSystem.Directory.CreateDirectory("/resources");
        fileSystem.File.WriteAllBytes("/resources/FFLResHigh.dat", [1, 2, 3]);
        var locator = Substitute.For<IMiiRenderingResourceLocator>();
        locator.GetFflResourcePath().Returns("/resources/FFLResHigh.dat");
        var renderer = new NativeMiiRenderer(locator, fileSystem);

        var result = renderer.RenderToBuffer(new Mii(), "", new MiiImageSpecifications());

        Assert.True(result.IsFailure);
        Assert.Contains("too small (3 bytes)", result.Error!.Message);
    }

    [Fact]
    public void ArchiveCache_BelongsToItsRendererInsteadOfCrossingServiceProviders()
    {
        var firstFileSystem = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        var secondFileSystem = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        firstFileSystem.Directory.CreateDirectory("/resources");
        secondFileSystem.Directory.CreateDirectory("/resources");
        var header = new byte[0x4A00];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x46465241);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 0x00070000);
        const string path = "/resources/FFLResHigh.dat";
        firstFileSystem.File.WriteAllBytes(path, header);
        secondFileSystem.File.WriteAllBytes(path, [1, 2, 3]);
        var locator = Substitute.For<IMiiRenderingResourceLocator>();
        locator.GetFflResourcePath().Returns(path);

        var first = new NativeMiiRenderer(locator, firstFileSystem).RenderToBuffer(new Mii(), "", new MiiImageSpecifications());
        var second = new NativeMiiRenderer(locator, secondFileSystem).RenderToBuffer(new Mii(), "", new MiiImageSpecifications());

        // The valid empty archive reaches studio-data validation; another renderer must still read its own archive.
        Assert.Equal("Studio data is empty.", first.Error!.Message);
        Assert.Contains("too small (3 bytes)", second.Error!.Message);
    }
}
