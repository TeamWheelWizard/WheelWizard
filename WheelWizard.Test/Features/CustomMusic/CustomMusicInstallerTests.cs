using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.CustomMusic.Library;
using WheelWizard.Models.Mods;
using WheelWizard.Mods;

namespace WheelWizard.Test.Features.CustomMusic;

public class CustomMusicInstallerTests
{
    private readonly MockFileSystem _fs = new();
    private readonly IModPaths _paths;
    private readonly IModManager _mods = Substitute.For<IModManager>();
    private readonly ObservableCollection<Mod> _modList = [];
    private readonly CustomMusicInstaller _installer;

    public CustomMusicInstallerTests()
    {
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(Path.Combine(Path.GetTempPath(), "custom-music-tests", Guid.NewGuid().ToString("N")));
        _paths = new ModPaths(location, _fs);
        _fs.Directory.CreateDirectory(_paths.RootFolderPath);

        _mods.Mods.Returns(_modList);
        _mods.ReloadAsync().Returns(Ok());
        _mods.SaveModsAsync().Returns(Ok());
        _mods.GetLowestActivePriority().Returns(_ => _modList.Count == 0 ? 0 : _modList.Min(mod => mod.Priority));
        _mods.AddModAsync(Arg.Any<Mod>()).Returns(call =>
        {
            _modList.Add(call.Arg<Mod>());
            return Ok();
        });
        _mods.DeleteModAsync(Arg.Any<Mod>()).Returns(call =>
        {
            _modList.Remove(call.Arg<Mod>());
            return Ok();
        });

        _installer = new CustomMusicInstaller(_mods, _paths, _fs, NullLogger<CustomMusicInstaller>.Instance);
    }

    private string ModFolder => _paths.GetModDirectoryPath(CustomMusicInstaller.ModTitle);

    [Fact]
    public async Task Apply_CreatesAnEnabledTopPriorityPatchWithEveryFileTheGameLooksFor()
    {
        _modList.Add(new Mod { Title = "Music Pack", Priority = 3, IsEnabled = true });
        var slots = GameMusicCatalog.Build().ToDictionary(slot => slot.Key);
        var luigi = Install(slots["game:n_circuit32"], "normal", "final");
        var waiting = Install(slots["game:o_wi-fi_waiting32"], "waiting", null);

        var result = await _installer.ApplyAsync([luigi, waiting]);

        Assert.True(result.IsSuccess);
        var mod = Assert.Single(_modList, m => m.Title == CustomMusicInstaller.ModTitle);
        Assert.True(mod.IsEnabled);
        Assert.Equal(2, mod.Priority); // beats the music pack (lower number wins)
        Assert.Equal(CustomMusicModState.Enabled, _installer.GetState());
        Assert.Equal("normal", Read("n_Circuit32_n.brstm"));
        Assert.Equal("final", Read("n_Circuit32_f.brstm"));
        Assert.Equal("waiting", Read("o_Wi-Fi_waiting32.brstm"));
    }

    [Fact]
    public async Task Apply_UsesTheNormalSongForTheFinalLapWhenThereIsNoFinalLapVersion()
    {
        var slot = GameMusicCatalog.Build().First(s => s.Key == "game:n_kinoko");

        await _installer.ApplyAsync([Install(slot, "normal", null)]);

        Assert.Equal("normal", Read("n_Kinoko_F.brstm"));
    }

    [Fact]
    public async Task Apply_RemovesMusicThatWasResetAndKeepsThePatchInfo()
    {
        var slots = GameMusicCatalog.Build().ToDictionary(slot => slot.Key);
        await _installer.ApplyAsync([Install(slots["game:n_circuit32"], "a", "b"), Install(slots["game:n_farm"], "c", "d")]);
        _fs.File.WriteAllText(_fs.Path.Combine(ModFolder, "Custom Music.ini"), "[Mod]");

        await _installer.ApplyAsync([Install(slots["game:n_farm"], "c", "d")]);

        Assert.False(_fs.File.Exists(_fs.Path.Combine(ModFolder, "n_Circuit32_n.brstm")));
        Assert.True(_fs.File.Exists(_fs.Path.Combine(ModFolder, "n_Farm_n.brstm")));
        Assert.True(_fs.File.Exists(_fs.Path.Combine(ModFolder, "Custom Music.ini")));
    }

    [Fact]
    public async Task Apply_DeletesThePatchWhenNoMusicIsLeft()
    {
        var slot = GameMusicCatalog.Build()[0];
        await _installer.ApplyAsync([Install(slot, "a", "b")]);

        Assert.True((await _installer.ApplyAsync([])).IsSuccess);

        Assert.DoesNotContain(_modList, mod => mod.Title == CustomMusicInstaller.ModTitle);
        Assert.Equal(CustomMusicModState.NotCreated, _installer.GetState());
    }

    [Fact]
    public async Task Apply_TurnsADisabledPatchBackOn()
    {
        var mod = new Mod { Title = CustomMusicInstaller.ModTitle, Priority = 0, IsEnabled = false };
        _modList.Add(mod);
        Assert.Equal(CustomMusicModState.Disabled, _installer.GetState());

        await _installer.ApplyAsync([Install(GameMusicCatalog.Build()[0], "a", "b")]);

        Assert.True(mod.IsEnabled);
        await _mods.Received(1).SaveModsAsync();
    }

    [Fact]
    public async Task FindConflicts_ReportsHigherPriorityPatchesWithTheSameMusicFile()
    {
        await _installer.ApplyAsync([Install(GameMusicCatalog.Build()[0], "ours", "ours")]);
        var ours = _modList.Single();
        ours.Priority = 5;
        _modList.Add(new Mod { Title = "Winner", Priority = 1, IsEnabled = true });
        _modList.Add(new Mod { Title = "Loser", Priority = 9, IsEnabled = true });
        foreach (var title in new[] { "Winner", "Loser" })
        {
            var folder = _fs.Path.Combine(_paths.GetModDirectoryPath(title), "strm");
            _fs.Directory.CreateDirectory(folder);
            _fs.File.WriteAllText(_fs.Path.Combine(folder, "N_CIRCUIT32_N.brstm"), title);
        }

        var conflict = Assert.Single(_installer.FindConflicts());

        Assert.Equal("Winner", conflict.ModTitle);
    }

    private InstalledMusic Install(MusicSlot slot, string normal, string? final)
    {
        var folder = _fs.Path.Combine(Path.GetTempPath(), "converted", Guid.NewGuid().ToString("N"));
        _fs.Directory.CreateDirectory(folder);
        var normalPath = _fs.Path.Combine(folder, "normal.brstm");
        _fs.File.WriteAllText(normalPath, normal);
        string? finalPath = null;
        if (final != null)
        {
            finalPath = _fs.Path.Combine(folder, "final.brstm");
            _fs.File.WriteAllText(finalPath, final);
        }

        return new InstalledMusic(slot, normalPath, finalPath);
    }

    private string Read(string fileName) => _fs.File.ReadAllText(_fs.Path.Combine(ModFolder, fileName));
}
