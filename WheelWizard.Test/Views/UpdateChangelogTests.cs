using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using NSubstitute;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared;
using WheelWizard.Views;
using WheelWizard.Views.Patterns;

namespace WheelWizard.Test.Views;

public class UpdateChangelogTests
{
    [Fact]
    public async Task LoadingNotes_CreatesAVisibleCard()
    {
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        var github = Substitute.For<IGitHubSingletonService>();
        github.GetReleaseNotesAsync(Arg.Any<string>()).Returns(Ok("<p>Release changes</p>"));
        var view = new UpdateChangelog(github, [new GithubRelease { TagName = "v2.5.9" }, new GithubRelease { TagName = "v2.5.8" }]);
        var load = typeof(UpdateChangelog).GetMethod("LoadNotesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await (Task)load.Invoke(view, null)!;

        var card = Assert.IsType<Border>(view.FindControl<TransitioningContentControl>("Cards")!.Content);
        Assert.NotNull(card.Background);
        Assert.True(card.FindControl<ScrollViewer>("NotesScroll")!.IsVisible);
        Assert.False(card.FindControl<TextBlock>("ReleaseTitle")!.IsVisible);

        var move = typeof(UpdateChangelog).GetMethod("MovePage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        move.Invoke(view, [1]);
        var cards = view.FindControl<TransitioningContentControl>("Cards")!;
        var older = Assert.IsType<Border>(cards.Content);
        Assert.NotSame(card, older);
        Assert.True(older.FindControl<ScrollViewer>("NotesScroll")!.IsVisible);
        Assert.True(older.FindControl<TextBlock>("ReleaseTitle")!.IsVisible);
        Assert.Equal("v2.5.8", older.FindControl<TextBlock>("ReleaseTitle")!.Text);
        Assert.NotNull(cards.PageTransition);
        Assert.False(cards.IsTransitionReversed);

        move.Invoke(view, [-1]);
        Assert.True(cards.IsTransitionReversed);
        Assert.False(Assert.IsType<Border>(cards.Content).FindControl<TextBlock>("ReleaseTitle")!.IsVisible);
    }
}
