using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Mods;
using WheelWizard.Mods.ViewModels;
using WheelWizard.Mods.Views;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Shared;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.UI.Test;

public class ModBrowserTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task SparsePagesYieldEvenBatchesAndContinueWithoutScrolling(int earlyMatches)
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        api.GetModSearchResults("", Arg.Any<int>())
            .Returns(call =>
            {
                var page = call.ArgAt<int>(1);
                var response = Results($"Mod {page}");
                response.Records[0].Id = page;
                response.Records[0].HasContentRatings = page > earlyMatches && page < 6;
                response.MetaData.IsComplete = page == 6;
                return OperationResult.Ok(response);
            });
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), Substitute.For<IModManager>());
        await model.SearchAsync();
        Assert.Equal(5, api.ReceivedCalls().Count());
        Assert.Equal(earlyMatches - earlyMatches % 2, model.Results.Count);
        Assert.True(model.CanLoadMore);
        Assert.False(model.IsEmpty);

        api.ClearReceivedCalls();
        using var owner = new UiTestHost { Height = 540 };
        owner.Body.Child = new ModBrowserPage(model, Substitute.For<INavigationService>(), Substitute.For<IModalFactory>());
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(6, api.ReceivedCalls().Count());
        Assert.Equal(earlyMatches + 1, model.Results.Count);
        Assert.False(model.CanLoadMore);
    }

    [AvaloniaFact]
    public async Task Subcategories_CombineWithinEachSelectedType()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), Substitute.For<IModManager>());
        var response = Results("Mario");
        response.Records = new[] { (10080, 10082), (10080, 10083), (10080, 10084), (2083, 2085), (2083, 2084), (799, 6534) }
            .Select(
                (category, i) =>
                {
                    var mod = Results($"Mod {i}").Records[0];
                    mod.Id = i;
                    mod.RootCategory.ProfileUrl = $"https://gamebanana.com/mods/cats/{category.Item1}";
                    mod.SubCategory = new() { Name = "Subcategory", ProfileUrl = $"https://gamebanana.com/mods/cats/{category.Item2}" };
                    return mod;
                }
            )
            .ToList();
        api.GetModSearchResults("", 1).Returns(OperationResult.Ok(response));
        model.DraftModTypes.Add(model.ModTypes.Single(type => type.Id == 10080));
        model.DraftModTypes.Add(model.ModTypes.Single(type => type.Id == 2083));
        model.DraftSkinTypes.Add(model.SkinTypes.Single(type => type.Id == 10082));
        model.DraftSkinTypes.Add(model.SkinTypes.Single(type => type.Id == 10083));
        model.DraftUiTypes.Add(model.UiTypes.Single(type => type.Id == 2085));
        await model.ApplyFiltersAsync();
        Assert.Equal(new[] { 0, 1, 3 }, model.Results.Select(item => item.Mod.Id));
        Assert.Equal(5, model.ActiveFilterCount);
        model.DraftModTypes.Clear();
        Assert.Empty(model.DraftSkinTypes);
        Assert.Empty(model.DraftUiTypes);
        model.PrepareFilters();
        Assert.Equal(2, model.DraftSkinTypes.Count);
        Assert.Single(model.DraftUiTypes);
        model.Deactivate();
    }

    [AvaloniaFact]
    public async Task InfiniteScroll_AppendsEvenBatchesAndStopsAtTheEnd()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var first = Results("First");
        first.Records = Enumerable
            .Range(1, 15)
            .Select(id =>
            {
                var mod = Results($"Mod {id}").Records[0];
                mod.Id = id;
                mod.HasContentRatings = id <= 2;
                return mod;
            })
            .ToList();
        first.MetaData.IsComplete = false;
        var last = Results("Last");
        last.Records[0].Id = 16;
        var finalMod = Results("Final").Records[0];
        finalMod.Id = 17;
        last.Records.Add(finalMod);
        var pending = new TaskCompletionSource<OperationResult<GameBananaSearchResults>>();
        api.GetModSearchResults("", 1).Returns(OperationResult.Ok(first));
        api.GetModSearchResults("", 2).Returns(pending.Task);
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), Substitute.For<IModManager>());
        var page = new ModBrowserPage(model, Substitute.For<INavigationService>(), Substitute.For<IModalFactory>());
        using var owner = new UiTestHost { Height = 540 };
        owner.Body.Child = page;
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12, model.Results.Count);
        var scroll = page.FindControl<ScrollViewer>("ResultsScroll")!;
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(model.IsLoading);
        Assert.Equal(12, model.Results.Count);
        pending.SetResult(OperationResult.Ok(last));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(15, model.Results.Count);
        Assert.False(model.CanLoadMore);
        await api.Received(1).GetModSearchResults("", 2);
    }

    [AvaloniaFact]
    public async Task Filters_ApplyTogetherAndDiscardUnappliedChanges()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var tracks = GameBananaModCategories.Types.Single(type => type.Id == 799);
        var response = Results("Track patch");
        response.Records[0].Tags = [new() { Title = "Patches" }];
        response.Records[0].RootCategory.ProfileUrl = tracks.Url;
        var other = Results("Skin patch").Records[0];
        other.Id = 2;
        other.RootCategory.ProfileUrl = "https://example.com/skins";
        other.Tags = response.Records[0].Tags;
        response.Records.Add(other);
        api.GetModSearchResults("Patches", 1, 799).Returns(OperationResult.Ok(response));
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), Substitute.For<IModManager>());
        model.PrepareFilters();
        model.DraftPatchesOnly = true;
        model.DraftModTypes.Add(tracks);
        Assert.Equal(0, model.ActiveFilterCount);
        await model.ApplyFiltersAsync();
        Assert.Equal(2, model.ActiveFilterCount);
        Assert.Equal("Track patch", Assert.Single(model.Results).Mod.Name);
        model.DraftPatchesOnly = false;
        model.PrepareFilters();
        Assert.True(model.DraftPatchesOnly);
        model.Deactivate();
    }

    [AvaloniaTheory]
    [InlineData(480)]
    [InlineData(656)]
    public async Task Browser_SearchesInPage_AndDetailsPreserveResults(double width)
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        api.GetModSearchResults(Arg.Any<string>(), 1).Returns(call => OperationResult.Ok(Results(call.ArgAt<string>(0))));
        api.GetModDetails(1).Returns(OperationResult.Ok(ModTestFixture.CreateMod(1, "Selected mod")));
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), Substitute.For<IModManager>());
        var navigation = Substitute.For<INavigationService>();
        var factory = Substitute.For<IModalFactory>();
        var modal = new ModIndependentWindow(ModTestFixture.CreateContent(api));
        factory.Create<ModIndependentWindow>().Returns(modal);
        var browser = new ModBrowserPage(model, navigation, factory);
        using var owner = new UiTestHost { Width = width, Height = 540 };
        owner.Body.Child = browser;
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.IsAssignableFrom<ILockedSidebarPage>(browser);
        await api.Received(1).GetModSearchResults("", 1);
        var input = browser.FindControl<InputField>("SearchInput")!;
        input.Focus();
        input.Text = "  Mario  ";
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.False(input.IsFocused);
        Assert.Equal("Mario", Assert.Single(model.Results).Mod.Name);
        input.Text = "Luigi";
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal("Luigi", Assert.Single(model.Results).Mod.Name);
        // Enough results to exercise scrolling while details are opened and closed.
        var first = model.Results[0];
        for (var i = 0; i < 5; i++)
            model.Results.Add(first);
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var scroll = browser.FindControl<ScrollViewer>("ResultsScroll")!;
        scroll.Offset = new Vector(0, 180);
        owner.UpdateLayout();
        var offset = scroll.Offset;
        Assert.True(offset.Y > 0);
        var cards = browser.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ModCard")).ToList();
        Assert.All(cards, card => Assert.True(card.Bounds.Width <= browser.Bounds.Width));
        var nativeWindows = 0;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((_, _) => nativeWindows++);
        cards[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(modal, owner.Modals.TopModal);
        Assert.Equal(0, nativeWindows);
        var closed = modal.ShowDialog();
        modal.Close();
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(browser, owner.Body.Child);
        Assert.Equal(offset, scroll.Offset);
        Assert.Equal(6, model.Results.Count);
        Assert.Empty(navigation.ReceivedCalls());
        browser.FindControl<ActionButton>("BackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        navigation.Received(1).NavigateTo<ModsPage>();
    }

    [AvaloniaFact]
    public async Task NewSearchAndLeavingPage_IgnorePendingResults()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var pending = new TaskCompletionSource<OperationResult<GameBananaSearchResults>>();
        api.GetModSearchResults("old", 1).Returns(pending.Task);
        api.GetModSearchResults("new", 1).Returns(OperationResult.Ok(Results("new")));
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), Substitute.For<IModManager>())
        {
            SearchText = "old",
        };
        var first = model.SearchAsync();
        model.SearchText = "new";
        await model.SearchAsync();
        pending.SetResult(OperationResult.Ok(Results("old")));
        await first;
        Assert.Equal("new", Assert.Single(model.Results).Mod.Name);
        pending = new();
        api.GetModSearchResults("old", 1).Returns(pending.Task);
        model.SearchText = "old";
        var leaving = model.SearchAsync();
        model.Deactivate();
        pending.SetResult(OperationResult.Ok(Results("late")));
        await leaving;
        Assert.Empty(model.Results);
        Assert.False(model.IsLoading);
    }

    [AvaloniaFact]
    public async Task FilteredBatch_RetainsPendingResultsWhenPageFails()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var response = Results("Patch");
        response.MetaData.IsComplete = false;
        response.Records[0].Tags = [new() { Title = "Patches" }];
        response.Records.Add(Results("Ordinary mod").Records[0]);
        api.GetModSearchResults("Patches", 1).Returns(OperationResult.Ok(response));
        api.GetModSearchResults("Patches", 2).Returns((OperationResult<GameBananaSearchResults>)new OperationError { Message = "Offline" });
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), Substitute.For<IModManager>())
        {
            PatchesOnly = true,
        };
        await model.SearchAsync();
        Assert.Empty(model.Results);
        Assert.True(model.HasError);
        Assert.Empty(model.Results);
        var next = Results("Second patch");
        next.Records[0].Id = 2;
        next.Records[0].Tags = [new() { Title = "Patches" }];
        api.GetModSearchResults("Patches", 2).Returns(OperationResult.Ok(next));
        await model.LoadMoreAsync();
        Assert.Equal(2, model.Results.Count);
        Assert.False(model.HasError);
        Assert.False(model.CanLoadMore);
        model.SearchText = "x";
        await model.SearchAsync();
        Assert.True(model.HasError);
        await api.DidNotReceive().GetModSearchResults("x", 1);
        model.Deactivate();
    }

    [AvaloniaFact]
    public async Task InstalledBadgeTracksInstallsAndRemovals()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        api.GetModSearchResults("", 1).Returns(OperationResult.Ok(Results("Installed mod")));
        var manager = Substitute.For<IModManager>();
        manager.IsModInstalled(1).Returns(true);
        var model = new ModBrowserViewModel(api, Substitute.For<IGameBananaMediaService>(), manager);
        await model.SearchAsync();
        var item = Assert.Single(model.Results);
        Assert.True(item.IsInstalled);
        foreach (var installed in new[] { false, true })
        {
            manager.IsModInstalled(1).Returns(installed);
            manager.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                manager,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(IModManager.Mods))
            );
            Assert.Equal(installed, item.IsInstalled);
        }
        model.Deactivate();
    }

    private static GameBananaSearchResults Results(string name) =>
        new()
        {
            MetaData = new()
            {
                RecordCount = 1,
                PerPage = 15,
                IsComplete = true,
            },
            Records =
            [
                new()
                {
                    Id = 1,
                    Name = name,
                    Version = "1.0",
                    ModelName = "Mod",
                    Tags = [],
                    ProfileUrl = "https://example.com/mod",
                    HasContentRatings = false,
                    DateAdded = 0,
                    DateModified = 0,
                    PreviewMedia = new(),
                    Author = new()
                    {
                        Name = "Author",
                        ProfileUrl = "https://example.com/author",
                        AvatarUrl = "",
                    },
                    Game = new()
                    {
                        Name = "Mario Kart Wii",
                        ProfileUrl = "https://example.com/game",
                        IconUrl = "",
                    },
                    RootCategory = new()
                    {
                        Name = "Tracks",
                        ProfileUrl = "https://example.com/category",
                        IconUrl = "",
                    },
                },
            ],
        };
}
