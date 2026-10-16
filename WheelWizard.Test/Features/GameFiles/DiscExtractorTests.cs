using System.Text;
using Testably.Abstractions.Testing;
using WheelWizard.GameFiles;

namespace WheelWizard.Test.Features.GameFiles;

public class DiscExtractorTests
{
    private static MockFileSystem Disk(SimulationMode mode = SimulationMode.Linux)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(mode));
        fs.Directory.CreateDirectory(Root(mode, "games"));
        fs.Directory.CreateDirectory(Root(mode, "out"));
        return fs;
    }

    private static string Root(SimulationMode mode, string name) => mode == SimulationMode.Windows ? $@"C:\{name}" : $"/{name}";

    [Fact]
    public void Extract_WritesTheLayoutDolphinBootsFrom()
    {
        var fs = Disk();
        var disc = new SyntheticWiiDisc();
        fs.File.WriteAllBytes("/games/mkw.iso", disc.BuildIso());

        var result = new DiscExtractor(fs).Extract("/games/mkw.iso", "/out");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var iso = fs.File.ReadAllBytes("/games/mkw.iso");
        Assert.Equal(iso[..0x100], fs.File.ReadAllBytes("/out/disc/header.bin"));
        Assert.Equal(iso[0x4E000..0x4E020], fs.File.ReadAllBytes("/out/disc/region.bin"));
        Assert.Equal(disc.Ticket, fs.File.ReadAllBytes("/out/DATA/ticket.bin"));
        Assert.Equal(disc.Tmd, fs.File.ReadAllBytes("/out/DATA/tmd.bin"));
        Assert.Equal(disc.Certificates, fs.File.ReadAllBytes("/out/DATA/cert.bin"));
        Assert.Equal(disc.H3, fs.File.ReadAllBytes("/out/DATA/h3.bin"));
        Assert.Equal(disc.Boot, fs.File.ReadAllBytes("/out/DATA/sys/boot.bin"));
        Assert.Equal(disc.Bi2, fs.File.ReadAllBytes("/out/DATA/sys/bi2.bin"));
        Assert.Equal(disc.Apploader, fs.File.ReadAllBytes("/out/DATA/sys/apploader.img"));
        Assert.Equal(disc.MainDol, fs.File.ReadAllBytes("/out/DATA/sys/main.dol"));
        Assert.Equal(disc.Fst, fs.File.ReadAllBytes("/out/DATA/sys/fst.bin"));
        foreach (var (path, data) in disc.Files)
            Assert.Equal(data, fs.File.ReadAllBytes("/out/DATA/files/" + path));
    }

    [Fact]
    public void Extract_ReportsTheHashesTheManifestPins()
    {
        var fs = Disk();
        var disc = new SyntheticWiiDisc();
        fs.File.WriteAllBytes("/games/mkw.iso", disc.BuildIso());

        var result = new DiscExtractor(fs).Extract("/games/mkw.iso", "/out");

        Assert.Equal(SyntheticWiiDisc.Sha256(disc.MainDol), result.Value.MainDolSha256);
        Assert.Equal(SyntheticWiiDisc.Sha256(disc.Files["rel/StaticR.rel"]), result.Value.StaticRelSha256);
    }

    [Theory]
    [InlineData("iso")]
    [InlineData("wbfs")]
    [InlineData("gcz")]
    [InlineData("ciso")]
    [InlineData("rvz")]
    public void Extract_EveryContainerGivesTheSameFiles(string format)
    {
        var fs = Disk();
        var disc = new SyntheticWiiDisc();
        fs.File.WriteAllBytes("/games/iso.iso", disc.BuildIso());
        fs.File.WriteAllBytes($"/games/other.{format}", disc.Build(format));
        var extractor = new DiscExtractor(fs);

        Assert.True(extractor.Extract("/games/iso.iso", "/out/iso").IsSuccess);
        var result = extractor.Extract($"/games/other.{format}", "/out/other");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var expected = Snapshot(fs, "/out/iso");
        Assert.Equal(expected.Keys.Order(), Snapshot(fs, "/out/other").Keys.Order());
        foreach (var (path, data) in Snapshot(fs, "/out/other"))
            Assert.Equal(expected[path], data);
    }

    [Theory]
    [InlineData(SimulationMode.Windows)]
    [InlineData(SimulationMode.Linux)]
    [InlineData(SimulationMode.MacOS)]
    public void Extract_WorksOnEveryPlatform(SimulationMode mode)
    {
        var fs = Disk(mode);
        var disc = new SyntheticWiiDisc();
        var image = fs.Path.Combine(Root(mode, "games"), "Mario Kart Wii (Europe) (En,Fr,De,Es,It).rvz");
        fs.File.WriteAllBytes(image, disc.Build("rvz"));
        var destination = fs.Path.Combine(Root(mode, "out"), "with spaces", "GAME");

        var result = new DiscExtractor(fs).Extract(image, destination);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(disc.MainDol, fs.File.ReadAllBytes(fs.Path.Combine(destination, "DATA", "sys", "main.dol")));
    }

    [Fact]
    public void Extract_OnlyReadsTheImage()
    {
        var fs = Disk();
        var iso = new SyntheticWiiDisc().BuildIso();
        fs.File.WriteAllBytes("/games/mkw.iso", iso);
        var written = fs.File.GetLastWriteTimeUtc("/games/mkw.iso");

        // Another program holding the image open for reading must not stop the extraction.
        using (fs.File.Open("/games/mkw.iso", FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(new DiscExtractor(fs).Extract("/games/mkw.iso", "/out").IsSuccess);

        Assert.Equal(iso, fs.File.ReadAllBytes("/games/mkw.iso"));
        Assert.Equal(written, fs.File.GetLastWriteTimeUtc("/games/mkw.iso"));
    }

    [Fact]
    public void Extract_ReadOnlyImageWorks()
    {
        var fs = Disk();
        fs.File.WriteAllBytes("/games/mkw.iso", new SyntheticWiiDisc().BuildIso());
        fs.FileInfo.New("/games/mkw.iso").IsReadOnly = true;

        Assert.True(new DiscExtractor(fs).Extract("/games/mkw.iso", "/out").IsSuccess);
        Assert.True(fs.FileInfo.New("/games/mkw.iso").IsReadOnly);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:")]
    [InlineData("a\\..\\..\\evil")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("star*")]
    [InlineData("tab\tname")]
    public void Extract_RefusesNamesThatCouldEscapeOrBreakOnSomePlatform(string name)
    {
        var fs = Disk();
        var disc = new SyntheticWiiDisc
        {
            Files = new() { ["rel/StaticR.rel"] = [1, 2, 3], [$"{name}/payload.bin"] = [9] },
        };
        fs.File.WriteAllBytes("/games/evil.iso", disc.BuildIso());

        var result = new DiscExtractor(fs).Extract("/games/evil.iso", "/out/GAME");

        Assert.True(result.IsFailure);
        Assert.False(fs.File.Exists("/payload.bin"));
        Assert.False(fs.File.Exists("/out/payload.bin"));
        Assert.False(fs.File.Exists("/evil/payload.bin"));
    }

    [Fact]
    public void Extract_RefusesTwoNamesACaseInsensitiveDriveWouldMerge()
    {
        var fs = Disk();
        var disc = new SyntheticWiiDisc
        {
            Files = new()
            {
                ["rel/StaticR.rel"] = [1],
                ["Race/a.szs"] = [2],
                ["race/A.szs"] = [3],
            },
        };
        fs.File.WriteAllBytes("/games/dupe.iso", disc.BuildIso());

        var result = new DiscExtractor(fs).Extract("/games/dupe.iso", "/out");

        Assert.True(result.IsFailure);
        Assert.Contains("capitalization", result.Error.Message);
    }

    [Fact]
    public void Extract_NeverOverwritesExistingFiles()
    {
        var fs = Disk();
        fs.File.WriteAllBytes("/games/mkw.iso", new SyntheticWiiDisc().BuildIso());
        fs.Directory.CreateDirectory("/out/DATA/sys");
        fs.File.WriteAllText("/out/DATA/sys/main.dol", "mine");

        Assert.True(new DiscExtractor(fs).Extract("/games/mkw.iso", "/out").IsFailure);
        Assert.Equal("mine", fs.File.ReadAllText("/out/DATA/sys/main.dol"));
    }

    [Fact]
    public void Extract_FailsWithoutStaticRel()
    {
        var fs = Disk();
        fs.File.WriteAllBytes("/games/other.iso", new SyntheticWiiDisc { Files = new() { ["a.bin"] = [1] } }.BuildIso());

        var result = new DiscExtractor(fs).Extract("/games/other.iso", "/out");

        Assert.True(result.IsFailure);
        Assert.Contains("StaticR.rel", result.Error.Message);
    }

    [Theory]
    [InlineData(0x8000)]
    [InlineData(0x50000)]
    [InlineData(0x60000)]
    public void Extract_FailsCleanlyOnATruncatedImage(int length)
    {
        var fs = Disk();
        fs.File.WriteAllBytes("/games/cut.iso", new SyntheticWiiDisc().BuildIso()[..length]);

        Assert.True(new DiscExtractor(fs).Extract("/games/cut.iso", "/out").IsFailure);
    }

    [Fact]
    public void Extract_RejectsAGameCubeOrGarbageFile()
    {
        var fs = Disk();
        var gamecube = new byte[0x50000];
        Encoding.ASCII.GetBytes("GALE01").CopyTo(gamecube, 0);
        new byte[] { 0xC2, 0x33, 0x9F, 0x3D }.CopyTo(gamecube, 0x1C);
        fs.File.WriteAllBytes("/games/melee.iso", gamecube);
        fs.File.WriteAllBytes("/games/notes.iso", Encoding.ASCII.GetBytes("not a disc"));
        var extractor = new DiscExtractor(fs);

        Assert.True(extractor.Extract("/games/melee.iso", "/out/a").IsFailure);
        Assert.True(extractor.Extract("/games/notes.iso", "/out/b").IsFailure);
        Assert.True(extractor.Extract("/games/missing.iso", "/out/c").IsFailure);
    }

    [Fact]
    public void Extract_ObservesCancellation()
    {
        var fs = Disk();
        fs.File.WriteAllBytes("/games/mkw.iso", new SyntheticWiiDisc().BuildIso());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => new DiscExtractor(fs).Extract("/games/mkw.iso", "/out", null, cancellation.Token)
        );
    }

    [Fact]
    public void Extract_ReportsProgressUpToTheEnd()
    {
        var fs = Disk();
        fs.File.WriteAllBytes("/games/mkw.iso", new SyntheticWiiDisc().BuildIso());
        var reports = new List<double>();

        new DiscExtractor(fs).Extract("/games/mkw.iso", "/out", new SyncProgress<double>(reports.Add));

        Assert.NotEmpty(reports);
        Assert.Equal(1, reports[^1]);
        Assert.Equal(reports.Order(), reports);
    }

    [Fact]
    public void Inspect_ReadsTheGameWithoutExtracting()
    {
        var fs = Disk();
        var disc = new SyntheticWiiDisc { GameId = "RMCE01", Revision = 1 };
        fs.File.WriteAllBytes("/games/mkw.wbfs", disc.Build("wbfs"));

        var contents = new DiscExtractor(fs).Inspect("/games/mkw.wbfs");

        Assert.True(contents.IsSuccess, contents.IsFailure ? contents.Error.Message : null);
        Assert.Equal("RMCE01", contents.Value.GameId);
        Assert.Equal(1, contents.Value.Revision);
        Assert.True(contents.Value.RequiredBytes >= disc.Files.Values.Sum(file => file.Length));
        Assert.Empty(fs.Directory.EnumerateFileSystemEntries("/out"));
    }

    [Fact]
    public void RestoreCode_ReplacesOnlyMainDolAndStaticRel()
    {
        var fs = Disk();
        var disc = new SyntheticWiiDisc();
        fs.File.WriteAllBytes("/games/mkw.iso", disc.BuildIso());
        var extractor = new DiscExtractor(fs);
        extractor.Extract("/games/mkw.iso", "/out");
        fs.File.WriteAllText("/out/DATA/sys/main.dol", "patched");
        fs.File.Delete("/out/DATA/files/rel/StaticR.rel");
        fs.File.WriteAllText("/out/DATA/files/Scene/UI/Title.szs", "my edit");

        var result = extractor.RestoreCode("/games/mkw.iso", "/out/DATA");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(disc.MainDol, fs.File.ReadAllBytes("/out/DATA/sys/main.dol"));
        Assert.Equal(disc.Files["rel/StaticR.rel"], fs.File.ReadAllBytes("/out/DATA/files/rel/StaticR.rel"));
        Assert.Equal("my edit", fs.File.ReadAllText("/out/DATA/files/Scene/UI/Title.szs"));
        Assert.Empty(fs.Directory.EnumerateFiles("/out", "*.restore", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("StaticR.rel", true)]
    [InlineData("Race", true)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("a:b", false)]
    [InlineData("LPT1", false)]
    [InlineData("aux.szs", false)]
    [InlineData("console.szs", true)]
    public void SafeName_UsesTheStrictestPlatformRules(string name, bool safe)
    {
        if (safe)
            Assert.Equal(name, DiscExtractor.SafeName(name));
        else
            Assert.Throws<InvalidDataException>(() => DiscExtractor.SafeName(name));
    }

    private static Dictionary<string, byte[]> Snapshot(MockFileSystem fs, string root) =>
        fs
            .Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => fs.Path.GetRelativePath(root, file).Replace('\\', '/'), file => fs.File.ReadAllBytes(file));
}

/// <summary>Reports on the calling thread, unlike <see cref="Progress{T}"/>, so a test sees every report in order.</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
