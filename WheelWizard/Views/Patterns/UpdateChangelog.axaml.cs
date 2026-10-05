using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using TheArtOfDev.HtmlRenderer.Avalonia;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;

namespace WheelWizard.Views.Patterns;

public partial class UpdateChangelog : UserControl
{
    private readonly IGitHubSingletonService _gitHub;
    private readonly IReadOnlyList<GithubRelease> _releases;
    private int _index;
    private int _request;
    private bool _closed;

    public UpdateChangelog(IGitHubSingletonService gitHub, IReadOnlyList<GithubRelease> releases)
    {
        InitializeComponent();
        _gitHub = gitHub;
        _releases = releases;
        CarouselNavigation.IsVisible = releases.Count > 1;
        foreach (var release in releases)
        {
            var dot = new Border();
            dot.Classes.Add("carouselDot");
            AutomationProperties.SetName(dot, $"Changelog {release.TagName}");
            CarouselDots.Children.Add(dot);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _closed = false;
        _ = LoadNotesAsync();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _closed = true;
        base.OnUnloaded(e);
    }

    private async Task LoadNotesAsync()
    {
        var request = ++_request;
        foreach (var (dot, index) in CarouselDots.Children.Select((dot, index) => (dot, index)))
            dot.Classes.Set("active", index == _index);
        if (_releases.Count == 0)
            return;

        var tag = _releases[_index].TagName;
        var template = (IControlTemplate)this.FindResource("ChangelogCardTemplate")!;
        var built = template.Build(Cards)!;
        var card = built.Result;
        NameScope.SetNameScope(card, built.NameScope);
        var releaseTitle = card.FindControl<TextBlock>("ReleaseTitle")!;
        var notesScroll = card.FindControl<ScrollViewer>("NotesScroll")!;
        var notesStatus = card.FindControl<StackPanel>("NotesStatus")!;
        var notesStatusText = card.FindControl<TextBlock>("NotesStatusText")!;
        var retryButton = card.FindControl<Views.Components.Button>("RetryButton")!;
        var releaseNotes = card.FindControl<HtmlPanel>("ReleaseNotes")!;
        releaseTitle.Text = tag;
        releaseTitle.IsVisible = _index != 0;
        notesScroll.IsVisible = false;
        retryButton.IsVisible = false;
        Cards.Content = card;
        var result = await _gitHub.GetReleaseNotesAsync(tag);
        if (_closed || request != _request)
            return;
        if (result.IsFailure)
        {
            notesStatusText.Text = "Couldn't load this changelog. Please try again.";
            retryButton.IsVisible = true;
            return;
        }

        releaseNotes.Text = $"<body>{result.Value}</body>";
        notesStatus.IsVisible = false;
        notesScroll.IsVisible = true;
    }

    private void MovePage(int offset)
    {
        if (_releases.Count < 2)
            return;
        Cards.IsTransitionReversed = offset < 0;
        _index = (_index + offset + _releases.Count) % _releases.Count;
        _ = LoadNotesAsync();
    }

    private void Previous_OnClick(object? sender, RoutedEventArgs e) => MovePage(-1);

    private void Next_OnClick(object? sender, RoutedEventArgs e) => MovePage(1);

    private void Retry_OnClick(object? sender, RoutedEventArgs e) => _ = LoadNotesAsync();
}
