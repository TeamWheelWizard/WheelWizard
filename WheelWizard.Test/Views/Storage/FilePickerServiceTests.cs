using System.Diagnostics;
using Avalonia.Platform.Storage;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.Views.Storage;

namespace WheelWizard.Test.Views.Storage;

public class FilePickerServiceTests
{
    [Fact]
    public async Task NoActiveWindow_ReturnsEmptySelections()
    {
        var service = new FilePickerService(
            Substitute.For<IStorageProviderAccessor>(),
            Substitute.For<IRuntimeEnvironment>(),
            Substitute.For<IProcessLauncher>()
        );
        Assert.Empty(await service.OpenFilePickerAsync(new("test")));
        Assert.Null(await service.OpenSingleFileAsync("test", []));
        Assert.Empty(await service.SelectFolderAsync("test"));
        Assert.Null(await service.SaveFileAsync("test", []));
    }

    [Fact]
    public async Task Selection_UsesCurrentWindowProviderAndOnlyReturnsLocalFiles()
    {
        var accessor = Substitute.For<IStorageProviderAccessor>();
        var first = Substitute.For<IStorageProvider>();
        var second = Substitute.For<IStorageProvider>();
        var local = Substitute.For<IStorageFile>();
        local.Path.Returns(new Uri("file:///tmp/mii%20name.mii"));
        var remote = Substitute.For<IStorageFile>();
        remote.Path.Returns(new Uri("https://example.com/mii.mii"));
        first.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(new List<IStorageFile> { remote, local });
        second.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(new List<IStorageFile>());
        accessor.Current.Returns(first);
        var service = new FilePickerService(accessor, Substitute.For<IRuntimeEnvironment>(), Substitute.For<IProcessLauncher>());

        Assert.Equal(["/tmp/mii name.mii"], await service.OpenFilePickerAsync(new("Miis"), false, "Import Mii"));
        await first
            .Received(1)
            .OpenFilePickerAsync(Arg.Is<FilePickerOpenOptions>(options => !options.AllowMultiple && options.Title == "Import Mii"));
        accessor.Current.Returns(second);
        Assert.Null(await service.OpenSingleFileAsync("Import", []));
        await second.Received(1).OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>());
    }

    [Fact]
    public async Task Save_PreservesNameFiltersAndOverwritePrompt()
    {
        var accessor = Substitute.For<IStorageProviderAccessor>();
        var provider = Substitute.For<IStorageProvider>();
        accessor.Current.Returns(provider);
        var selected = Substitute.For<IStorageFile>();
        selected.Path.Returns(new Uri("file:///tmp/export.mii"));
        provider.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(selected);
        var service = new FilePickerService(accessor, Substitute.For<IRuntimeEnvironment>(), Substitute.For<IProcessLauncher>());
        var filter = new FilePickerFileType("Miis") { Patterns = ["*.mii"] };

        Assert.Equal("/tmp/export.mii", await service.SaveFileAsync("Export", [filter], "export.mii"));

        await provider
            .Received(1)
            .SaveFilePickerAsync(
                Arg.Is<FilePickerSaveOptions>(options =>
                    options.ShowOverwritePrompt == true
                    && options.SuggestedFileName == "export.mii"
                    && options.FileTypeChoices!.Contains(filter)
                )
            );
    }

    [Theory]
    [InlineData("windows", "explorer.exe")]
    [InlineData("linux", "xdg-open")]
    [InlineData("macos", "open")]
    public void OpenFolder_UsesInjectedPlatform_AndOneLiteralArgument(string platform, string executable)
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(platform == "windows");
        environment.IsLinux.Returns(platform == "linux");
        environment.IsMacOS.Returns(platform == "macos");
        var processes = Substitute.For<IProcessLauncher>();
        var service = new FilePickerService(Substitute.For<IStorageProviderAccessor>(), environment, processes);

        service.OpenFolderInFileManager("folder with spaces and 'quotes'");

        processes
            .Received(1)
            .Start(
                Arg.Is<ProcessStartInfo>(info =>
                    info.FileName == executable && info.ArgumentList.Count == 1 && info.ArgumentList[0] == "folder with spaces and 'quotes'"
                )
            );
    }
}
