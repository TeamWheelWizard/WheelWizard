using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Mods;
using WheelWizard.Shared;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Services;
using WheelWizard.Views.ModManagement;
using WheelWizard.Views.Navigation;
using WheelWizard.Views.Patterns;
using WheelWizard.Views.Popups.ModManagement;

namespace WheelWizard.UI.Test;

public class ModBrowserPreviewTests
{
    [AvaloniaFact]
    public async Task ClosedBrowser_DiscardsPendingSearchResults()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        var response = new TaskCompletionSource<OperationResult<GameBananaSearchResults>>();
        mods.GetModSearchResults(Arg.Any<string>(), Arg.Any<int>()).Returns(response.Task);
        var details = new ModContent(
            Substitute.For<IModManager>(),
            mods,
            Substitute.For<IDownloadService>(),
            media,
            Substitute.For<IModPaths>(),
            Substitute.For<IModOperationPresentation>()
        );
        var browser = new ModBrowserWindow(details, mods, Substitute.For<INavigationService>(), media);
        try
        {
            browser.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await mods.Received(1).GetModSearchResults("", 1);
            browser.Close();
            var mod = new GameBananaSingletonService(Substitute.For<IApiCaller<IGameBananaApi>>()).GetLoadingPreview();
            mod.ModelName = "Mod";
            mod.Name = "Late result";
            response.SetResult(
                new GameBananaSearchResults
                {
                    MetaData = new GameBananaSearchMetaData
                    {
                        RecordCount = 1,
                        PerPage = 15,
                        IsComplete = true,
                    },
                    Records = [mod],
                }
            );
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.Empty(browser.FindControl<ListBox>("ModListView")!.Items);
            Assert.Empty(media.ReceivedCalls());
        }
        finally
        {
            browser.Close();
        }
    }

    [AvaloniaFact]
    public async Task RecycledCard_LoadsAndBindsItsCurrentPreview()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jvX0AAAAASUVORK5CYII=");
        media.GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((OperationResult<byte[]>)png);
        using var first = new ModPreviewViewModel(1, mods, media, "https://images.test/first.png");
        using var second = new ModPreviewViewModel(2, mods, media, "https://images.test/second.png");
        var card = new ModBrowserListItem { Preview = first };
        var window = new Window
        {
            Content = card,
            Width = 300,
            Height = 100,
        };
        try
        {
            Assert.Empty(media.ReceivedCalls());
            window.Show();
            window.UpdateLayout();
            await first.LoadAsync();
            var image = Assert.Single(card.GetVisualDescendants().OfType<Image>());
            Assert.NotNull(first.Image);
            Assert.Same(first.Image, image.Source);

            card.Preview = second;
            await second.LoadAsync();
            window.UpdateLayout();
            Assert.NotNull(second.Image);
            Assert.Same(second.Image, image.Source);
            card.Preview = null;
            Assert.Null(image.Source);
            Assert.Empty(mods.ReceivedCalls());
        }
        finally
        {
            window.Close();
        }
    }
}
