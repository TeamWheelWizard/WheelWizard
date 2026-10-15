using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NSubstitute;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Mods.Views;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Shared;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.UI.Test;

public class ModBrowserTests
{
    [AvaloniaFact]
    public async Task NativeBrowser_LoadsInitially_AndSearchesByEnterAndButton()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        api.GetModSearchResults(Arg.Any<string>(), Arg.Any<int>()).Returns(call => OperationResult.Ok(Results(call.ArgAt<string>(0))));
        var browser = CreateBrowser(api, Substitute.For<INavigationService>());
        browser.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var list = browser.FindControl<ListBox>("ModListView")!;
            await api.Received(1).GetModSearchResults("", 1);
            Assert.Single(list.Items);

            var input = browser.FindControl<InputField>("SearchTextBox")!;
            input.Text = "  Mario  ";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await api.Received(1).GetModSearchResults("Mario", 1);
            Assert.Equal("Mario", Assert.IsType<ModSearchResult>(Assert.Single(list.Items)).Mod.Name);

            input.Text = "Luigi";
            ((Grid)input.Parent!).Children.OfType<ActionButton>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await api.Received(1).GetModSearchResults("Luigi", 1);
            Assert.Equal("Luigi", Assert.IsType<ModSearchResult>(Assert.Single(list.Items)).Mod.Name);
        }
        finally
        {
            browser.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClosingBrowser_DiscardsPendingSearch_AndRefreshesMods()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var pending = new TaskCompletionSource<OperationResult<GameBananaSearchResults>>();
        api.GetModSearchResults("", 1).Returns(pending.Task);
        var navigation = Substitute.For<INavigationService>();
        var browser = CreateBrowser(api, navigation);
        browser.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await api.Received(1).GetModSearchResults("", 1);
            browser.Close();
            pending.SetResult(OperationResult.Ok(Results("Late result")));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(browser.FindControl<ListBox>("ModListView")!.Items);
            navigation.Received(1).NavigateTo<ModsPage>();
        }
        finally
        {
            browser.Close();
        }
    }

    private static ModBrowserWindow CreateBrowser(IGameBananaSingletonService api, INavigationService navigation)
    {
        var media = Substitute.For<IGameBananaMediaService>();
        var content = ModTestFixture.CreateContent(api, media);
        return new(content, api, navigation, media);
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
