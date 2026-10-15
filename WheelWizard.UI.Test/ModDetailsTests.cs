using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Mods.ViewModels;
using WheelWizard.Mods.Views;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Shared;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using static WheelWizard.UI.Test.ModTestFixture;
using Spinner = WheelWizard.Views.Components.Spinner;

namespace WheelWizard.UI.Test;

public class ModDetailsTests
{
    [AvaloniaTheory]
    [InlineData(480, 420)]
    [InlineData(656, 876)]
    [InlineData(1100, 900)]
    public async Task Details_StayInsideOwner_WithSeparateTitleStatsAndVisibleActions(double width, double height)
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var mod = CreateMod(1, string.Join(" ", Enumerable.Repeat("A very long mod title", 6)));
        api.GetModDetails(1).Returns(OperationResult.Ok(mod));
        var content = CreateContent(api);
        using var owner = new TestWindow { Width = width, Height = height };
        owner.Show();
        var nativeWindows = 0;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((_, _) => nativeWindows++);
        var modal = new ModIndependentWindow(content);
        modal.SetModalOwner(owner);
        var closed = modal.ShowDialog();
        await modal.LoadModAsync(1);
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
        Assert.Equal(0, nativeWindows);
        Assert.Same(owner, TopLevel.GetTopLevel(modal));
        var title = content.FindControl<TextBlock>("ModTitle")!;
        Assert.Equal(mod.Name, title.Text);
        var report = content.FindControl<ActionButton>("ReportButton")!;
        Assert.Equal(ActionButtonTone.Danger, report.Tone);
        Assert.False(report.IsIconLeft);
        var links = content.FindControl<WrapPanel>("ModLinks")!;
        Assert.True(report.TranslatePoint(default, links)!.Value.X >= links.Bounds.Width);
        var stats = content.FindControl<WrapPanel>("ModStats")!;
        Assert.True(stats.TranslatePoint(default, title)!.Value.Y >= title.Bounds.Height);
        var action = content.FindControl<ActionButton>("InstallButton")!;
        var position = action.TranslatePoint(default, owner)!.Value;
        Assert.True(action.Bounds.Width > 0);
        Assert.InRange(position.X, 0, owner.ClientSize.Width - action.Bounds.Width);
        Assert.InRange(position.Y, 0, owner.ClientSize.Height - action.Bounds.Height);
        var scroll = content.FindControl<ScrollViewer>("DetailsScroll")!;
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        var header = content.FindControl<StackPanel>("DetailsHeader")!;
        var beforeScroll = header.TranslatePoint(default, owner);
        scroll.Offset = new Vector(0, 200);
        owner.UpdateLayout();
        Assert.Equal(beforeScroll, header.TranslatePoint(default, owner));
        Assert.True(scroll.Offset.Y > 0);
        Assert.Equal(header.Bounds.Width, stats.Bounds.Right, 1);
        var bar = scroll
            .GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && b.TemplatedParent == scroll);
        Assert.True(bar.IsEffectivelyVisible);
        var barPosition = bar.TranslatePoint(default, content)!.Value;
        Assert.True(barPosition.X + bar.Bounds.Width <= content.Bounds.Width);
        Assert.False(content.FindControl<StackPanel>("ImagesSection")!.IsVisible);
        owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(owner.Modals.HasModals);
    }

    [AvaloniaFact]
    public async Task SwitchingMods_IgnoresLateDetailsFromPreviousSelection()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var pending = new TaskCompletionSource<OperationResult<GameBananaModDetails>>();
        api.GetModDetails(1).Returns(pending.Task);
        api.GetModDetails(2).Returns(OperationResult.Ok(CreateMod(2, "New selection")));
        var content = CreateContent(api);
        var first = content.LoadModDetailsAsync(1);
        Assert.True(await content.LoadModDetailsAsync(2));
        pending.SetResult(OperationResult.Ok(CreateMod(1, "Old selection")));
        Assert.False(await first);
        Assert.Equal("New selection", content.FindControl<TextBlock>("ModTitle")!.Text);
        Assert.False(content.FindControl<Spinner>("LoadingView")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task Gallery_ShowsDetailsWhileLoading_AndCancelsWhenModalCloses()
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        var mod = CreateMod(1, "Loading screenshots", imageCount: 3);
        api.GetModDetails(1).Returns(OperationResult.Ok(mod));
        var pending = new TaskCompletionSource<OperationResult<byte[]>>();
        CancellationToken imageToken = default;
        media
            .GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                imageToken = call.Arg<CancellationToken>();
                return pending.Task;
            });
        var content = CreateContent(api, media);
        using var owner = new TestWindow();
        owner.Show();
        var modal = new ModIndependentWindow(content);
        modal.SetModalOwner(owner);
        var closed = modal.ShowDialog();
        var loading = content.LoadModDetailsAsync(1);
        Assert.True(content.FindControl<Grid>("DetailsView")!.IsVisible);
        Assert.True(content.FindControl<Spinner>("ImagesLoading")!.IsVisible);
        Assert.Equal("1 / 3", content.FindControl<TextBlock>("ImagePosition")!.Text);
        var placeholders = content.FindControl<ListBox>("ImageCarousel")!.Items;
        Assert.Equal(3, placeholders.Count);
        Assert.All(placeholders.OfType<ModScreenshotViewModel>(), image => Assert.True(image.IsLoading));
        Assert.True(content.FindControl<ModImagePlaceholder>("BannerLoading")!.IsVisible);
        modal.Close();
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(imageToken.IsCancellationRequested);
        pending.SetResult(new OperationError { Message = "Image unavailable" });
        Assert.False(await loading);
        Assert.Empty(content.FindControl<ItemsControl>("ImageCarousel")!.Items);
    }

    [AvaloniaTheory]
    [InlineData(480, 420)]
    [InlineData(656, 876)]
    public async Task Gallery_SelectionAndFailedImagesKeepTheirPositions(double width, double height)
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        var media = Substitute.For<IGameBananaMediaService>();
        var mod = CreateMod(1, "A custom track collection with a longer title", imageCount: 3);
        api.GetModDetails(1).Returns(OperationResult.Ok(mod));
        using var asset = Avalonia.Platform.AssetLoader.Open(new Uri("avares://WheelWizard/Resources/Images/WiiCompiled.png"));
        using var bytes = new MemoryStream();
        asset.CopyTo(bytes);
        media.GetImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok(bytes.ToArray()));
        media
            .GetImageAsync("https://example.com/2.png", Arg.Any<CancellationToken>())
            .Returns(new OperationError { Message = "Missing image" });
        var content = CreateContent(api, media);
        using var owner = new TestWindow { Width = width, Height = height };
        owner.Show();
        var modal = new ModIndependentWindow(content);
        modal.SetModalOwner(owner);
        modal.Show();
        await content.LoadModDetailsAsync(1);
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
        Assert.Equal(0, content.FindControl<ScrollViewer>("DetailsScroll")!.Offset.Y);
        var gallery = content.FindControl<ItemsControl>("ImageCarousel")!;
        Assert.Equal(3, gallery.Items.Count);
        var navigation = content.FindControl<Grid>("GalleryNavigation")!;
        var section = content.FindControl<StackPanel>("ImagesSection")!;
        Assert.Equal((section.Bounds.Width - navigation.Bounds.Width) / 2, navigation.TranslatePoint(default, section)!.Value.X, 1);
        var previous = content.FindControl<ActionButton>("PreviousImage")!;
        var next = content.FindControl<ActionButton>("NextImage")!;
        Assert.True(previous.TranslatePoint(default, gallery)!.Value.X + previous.Bounds.Width <= 0);
        Assert.True(next.TranslatePoint(default, gallery)!.Value.X >= gallery.Bounds.Width);
        Assert.True(Assert.IsType<ModScreenshotViewModel>(gallery.Items[1]).HasFailed);
        var last = Assert.IsType<ModScreenshotViewModel>(gallery.Items[2]);
        ((ListBox)gallery).SelectedIndex = 2;
        Assert.Same(last.Image, content.FindControl<Image>("BannerImage")!.Source);
        Assert.Equal("3 / 3", content.FindControl<TextBlock>("ImagePosition")!.Text);
        Assert.False(content.FindControl<ActionButton>("NextImage")!.IsEnabled);
        content.FindControl<ActionButton>("PreviousImage")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(content.FindControl<PathIcon>("BannerUnavailable")!.IsVisible);
        Assert.True(content.FindControl<Border>("BannerFrame")!.IsVisible);
        Assert.False(content.FindControl<Spinner>("ImagesLoading")!.IsVisible);
        owner.Close();
        Assert.Empty(content.FindControl<ItemsControl>("ImageCarousel")!.Items);
        Assert.Null(content.FindControl<Image>("BannerImage")!.Source);
    }

    [AvaloniaTheory]
    [InlineData(100, 200)]
    [InlineData(400, 200)]
    [InlineData(1600, 100)]
    public async Task MainImage_UsesNaturalWidthAtFixedHeight_AndFitsViewport(int width, int height)
    {
        var api = Substitute.For<IGameBananaSingletonService>();
        api.GetModDetails(1).Returns(OperationResult.Ok(CreateMod(1, "Image sizing")));
        var content = CreateContent(api);
        using var owner = new TestWindow();
        owner.Show();
        var modal = new ModIndependentWindow(content);
        modal.SetModalOwner(owner);
        modal.Show();
        await content.LoadModDetailsAsync(1);
        var screenshot = new ModScreenshotViewModel("Test image");
        screenshot.Complete(new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96)));
        var gallery = content.FindControl<ListBox>("ImageCarousel")!;
        gallery.Items.Add(screenshot);
        gallery.SelectedIndex = 0;
        content.FindControl<StackPanel>("ImagesSection")!.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
        var frame = content.FindControl<Border>("BannerFrame")!;
        var container = content.FindControl<Grid>("BannerContainer")!;
        var expectedWidth = Math.Min((double)width / height * 258 + 2, container.Bounds.Width);
        Assert.Equal(260, frame.Bounds.Height);
        Assert.InRange(frame.Bounds.Width, expectedWidth - 1, expectedWidth + 1);
        Assert.InRange(
            Math.Abs((container.Bounds.Width - frame.Bounds.Width) / 2 - frame.TranslatePoint(default, container)!.Value.X),
            0,
            1
        );
    }

    private sealed class TestWindow : BaseWindow, IDisposable
    {
        public void Dispose() => Close();

        private readonly Grid _body = new();
        private readonly Border _overlay = new();
        protected override Control InteractionContent => _body;
        protected override Control InteractionOverlay => _overlay;

        public TestWindow()
        {
            Width = 656;
            Height = 876;
            Content = new Grid { Children = { _body, _overlay } };
        }
    }
}
