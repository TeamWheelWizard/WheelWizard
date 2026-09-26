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
    public async Task NewSearch_DiscardsOlderResultsAndDisposesReplacedPreviews()
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        var older = new TaskCompletionSource<OperationResult<GameBananaSearchResults>>();
        var newer = new TaskCompletionSource<OperationResult<GameBananaSearchResults>>();
        mods.GetModSearchResults("", 1).Returns(older.Task);
        mods.GetModSearchResults("new", 1).Returns(newer.Task);
        mods.GetModSearchResults("empty", 1).Returns(SearchResults([]));
        var image = new TaskCompletionSource<OperationResult<byte[]>>();
        CancellationToken imageToken = default;
        media
            .GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                imageToken = call.Arg<CancellationToken>();
                return image.Task;
            });
        var browser = CreateBrowser(mods, media);
        try
        {
            browser.Show();
            await Flush();
            Search(browser, "new");
            await Flush();
            newer.SetResult(SearchResults([Preview(2, "New result")]));
            await Flush();
            var item = Assert.Single(browser.FindControl<ListBox>("ModListView")!.Items.Cast<ModSearchResult>());
            Assert.Equal(2, item.Mod.Id);
            var load = item.Preview!.LoadAsync();
            older.SetResult(SearchResults([Preview(1, "Old result")]));
            await Flush();
            Assert.Equal(2, Assert.Single(browser.FindControl<ListBox>("ModListView")!.Items.Cast<ModSearchResult>()).Mod.Id);
            Search(browser, "empty");
            await Flush();
            Assert.True(imageToken.IsCancellationRequested);
            await load;
            image.SetResult(new byte[] { 1, 2, 3 });
            await Flush();
            Assert.Null(item.Preview.Image);
            Assert.Empty(browser.FindControl<ListBox>("ModListView")!.Items);
        }
        finally
        {
            browser.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchOnlyPaging_StopsAfterAMatchOrFailure(bool fail)
    {
        var mods = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        mods.GetModSearchResults("", 1).Returns(SearchResults([]));
        mods.GetModSearchResults("Patches", 1).Returns(SearchResults([Preview(1, "Non-patch")], false));
        var patch = Preview(2, "Patch");
        patch.Tags = [new() { Title = "Patches" }];
        var stop = new TaskCompletionSource<OperationResult<GameBananaSearchResults>>();
        OperationResult<GameBananaSearchResults> page = fail ? new OperationError { Message = "Offline" } : SearchResults([patch]);
        mods.GetModSearchResults("Patches", 2).Returns(_ => Task.FromResult(page), _ => stop.Task);
        var browser = CreateBrowser(mods, media);
        try
        {
            browser.Show();
            await Flush();
            var toggle = browser.FindControl<CheckBox>("PatchesOnlyToggle")!;
            toggle.IsChecked = true;
            toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Flush();
            await mods.Received(1).GetModSearchResults("Patches", 1);
            await mods.Received(1).GetModSearchResults("Patches", 2);
            if (!fail)
                Assert.Equal(2, Assert.Single(browser.FindControl<ListBox>("ModListView")!.Items.Cast<ModSearchResult>()).Mod.Id);
        }
        finally
        {
            browser.Close();
            stop.TrySetResult(SearchResults([]));
            await Flush();
        }
    }

    private static ModBrowserWindow CreateBrowser(IGameBananaSingletonService mods, IGameBananaMediaService media) =>
        new(
            new ModContent(
                Substitute.For<IModManager>(),
                mods,
                Substitute.For<IDownloadService>(),
                media,
                Substitute.For<IModPaths>(),
                Substitute.For<IModOperationPresentation>()
            ),
            mods,
            Substitute.For<INavigationService>(),
            media
        );

    private static void Search(ModBrowserWindow browser, string query)
    {
        var text = browser.FindControl<TextBox>("SearchTextBox")!;
        text.Text = query;
        text.RaiseEvent(
            new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }
        );
    }

    private static GameBananaSearchResults SearchResults(List<GameBananaModPreview> records, bool complete = true) =>
        new()
        {
            MetaData = new()
            {
                RecordCount = records.Count,
                PerPage = 15,
                IsComplete = complete,
            },
            Records = records,
        };

    private static GameBananaModPreview Preview(int id, string name)
    {
        var mod = new GameBananaSingletonService(Substitute.For<IApiCaller<IGameBananaApi>>()).GetLoadingPreview();
        mod.Id = id;
        mod.ModelName = "Mod";
        mod.Name = name;
        mod.PreviewMedia = new()
        {
            Images =
            [
                new()
                {
                    Type = "image",
                    BaseUrl = "https://images.test",
                    File = $"{id}.png",
                },
            ],
        };
        return mod;
    }

    private static async Task Flush()
    {
        await Task.Yield();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
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
