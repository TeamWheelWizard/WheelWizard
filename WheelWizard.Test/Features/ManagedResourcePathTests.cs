using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Mods;

namespace WheelWizard.Test.Features;

public class ManagedResourcePathTests
{
    [Fact]
    public async Task ModLoading_UsesRelocatedRoot_AfterServiceConstruction()
    {
        var fs = new MockFileSystem();
        var location = Substitute.For<IApplicationDataLocation>();
        var root = fs.Path.GetFullPath("/first");
        location.DirectoryPath.Returns(_ => root);
        var service = new ModInstallationService(fs, new ModPaths(location, fs));

        Assert.True((await service.LoadModsAsync()).IsSuccess);
        root = fs.Path.GetFullPath("/relocated");
        Assert.True((await service.LoadModsAsync()).IsSuccess);

        Assert.True(fs.Directory.Exists(fs.Path.Combine(root, "Mods")));
    }

    [Fact]
    public void ResourceLocator_UsesNewLocation_EvenIfOldCopyStillExists()
    {
        var fs = new MockFileSystem();
        var location = Substitute.For<IApplicationDataLocation>();
        var root = fs.Path.GetFullPath("/first");
        location.DirectoryPath.Returns(_ => root);
        var paths = new MiiRenderingPaths(location, fs);
        var locator = new MiiRenderingResourceLocator(fs, new MiiRenderingConfiguration { MinimumExpectedSizeBytes = 4 }, paths);
        var original = paths.ManagedResourcePath;
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(original)!);
        fs.File.WriteAllBytes(original, [1, 2, 3, 4]);
        Assert.Equal(original, locator.GetFflResourcePath().Value);

        root = fs.Path.GetFullPath("/relocated");
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(paths.ManagedResourcePath)!);
        fs.File.WriteAllBytes(paths.ManagedResourcePath, [4, 3, 2, 1]);

        Assert.Equal(paths.ManagedResourcePath, locator.GetFflResourcePath().Value);
        Assert.True(fs.File.Exists(original));
    }

    [Fact]
    public async Task ResourceInstallation_KeepsOneDestination_ForTheWholeOperation()
    {
        var fs = new MockFileSystem();
        var location = Substitute.For<IApplicationDataLocation>();
        var root = fs.Path.GetFullPath("/first");
        location.DirectoryPath.Returns(_ => root);
        var paths = new MiiRenderingPaths(location, fs);
        var initialTarget = paths.ManagedResourcePath;
        var api = Substitute.For<IMiiRenderingAssetApi>();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("asset/model/character/mii/AFLResHigh_2_3.dat").Open();
            entry.Write([1, 2, 3, 4]);
        }
        api.DownloadArchiveAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                root = fs.Path.GetFullPath("/relocated");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) });
            });
        var installer = new MiiRenderingResourceInstaller(
            api,
            Substitute.For<IMiiRenderingResourceLocator>(),
            fs,
            new MiiRenderingConfiguration { MinimumExpectedSizeBytes = 4 },
            paths,
            NullLogger<MiiRenderingResourceInstaller>.Instance
        );

        var result = await installer.DownloadAndInstallAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(initialTarget, result.Value);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, fs.File.ReadAllBytes(initialTarget));
        Assert.False(fs.File.Exists(paths.ManagedResourcePath));
        Assert.False(fs.File.Exists(fs.Path.Combine(fs.Path.GetDirectoryName(initialTarget)!, "FFLResHigh.dat.partial")));
    }
}
