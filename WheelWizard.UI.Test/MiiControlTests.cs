using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using Testably.Abstractions;
using WheelWizard.MiiAnimations;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiImages.Views;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
using WheelWizard.Shared.Calendar;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.UI.Test;

public class MiiControlTests
{
    [AvaloniaFact]
    public void Templates_ConstructAllRenderingViewsWithoutAGlobalServiceProvider()
    {
        var images = InstallThemes();
        var normal = new MiiImageLoader { Width = 100, Height = 100 };
        var hover = new MiiImageLoaderWithHover { Width = 100, Height = 100 };
        var interactive = new Mii3DRender
        {
            Width = 100,
            Height = 100,
            Interactive = false,
        };
        var animated = new MiiAnimatedImage
        {
            Width = 100,
            Height = 100,
            Performance = MiiPerformances.SidebarPlayer,
        };
        var window = new Window { Content = new StackPanel { Children = { normal, hover, interactive, animated } } };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Single(normal.GetVisualDescendants().OfType<MiiImageView>());
            Assert.Single(hover.GetVisualDescendants().OfType<MiiHoverView>());
            var render = Assert.Single(interactive.GetVisualDescendants().OfType<MiiRenderView>());
            Assert.False(render.Interactive);
            interactive.Interactive = true;
            Assert.True(render.Interactive);
            // Animations are off in these tests, so the animated Mii is its still picture.
            var still = Assert.Single(animated.GetVisualDescendants().OfType<MiiImageView>());
            Assert.True(still.IsVisible);
            Assert.Empty(animated.GetVisualDescendants().OfType<MiiRealtimeView>());
            images.DidNotReceiveWithAnyArgs().GetImageAsync(default, default!);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ImageRequests_FollowBoundInputsAndReloadAfterReattachment()
    {
        var images = InstallThemes();
        var first = MiiFactory.CreateRandomMii(new Testably.Abstractions.RealRandomSystem().Random.New(1));
        var second = MiiFactory.CreateRandomMii(new Testably.Abstractions.RealRandomSystem().Random.New(2));
        var control = new MiiImageLoader
        {
            Width = 100,
            Height = 100,
            Mii = first,
        };
        var window = new Window { Content = control };
        try
        {
            await images.DidNotReceiveWithAnyArgs().GetImageAsync(default, default!);
            window.Show();
            window.UpdateLayout();
            await images.Received().GetImageAsync(first, Arg.Any<MiiImageSpecifications>());

            control.Mii = second;
            await images.Received().GetImageAsync(second, Arg.Any<MiiImageSpecifications>());
            images.ClearReceivedCalls();
            window.Content = null;
            control.Mii = first;
            await images.DidNotReceiveWithAnyArgs().GetImageAsync(default, default!);
            window.Content = control;
            window.UpdateLayout();
            await images.Received().GetImageAsync(first, Arg.Any<MiiImageSpecifications>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DetachedImage_IgnoresLateCompletion()
    {
        var images = InstallThemes();
        var pending = new TaskCompletionSource<OperationResult<Bitmap>>();
        images.GetImageAsync(Arg.Any<Mii>(), Arg.Any<MiiImageSpecifications>()).Returns(pending.Task);
        var control = new MiiImageLoader
        {
            Width = 100,
            Height = 100,
            Mii = MiiFactory.CreateRandomMii(new Testably.Abstractions.RealRandomSystem().Random.New(1)),
        };
        var loaded = 0;
        control.MiiImageLoaded += (_, _) => ++loaded;
        var window = new Window { Content = control };
        window.Show();
        window.UpdateLayout();
        window.Close();
        pending.SetResult(new OperationError { Message = "No image" });
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal(0, loaded);
    }

    [AvaloniaFact]
    public async Task Hover_UsesTheCurrentMiiAndUpdatedVariant()
    {
        var images = InstallThemes();
        var first = MiiFactory.CreateRandomMii(new Testably.Abstractions.RealRandomSystem().Random.New(1));
        var second = MiiFactory.CreateRandomMii(new Testably.Abstractions.RealRandomSystem().Random.New(2));
        var normal = MiiImageVariants.MiiListTile.Clone();
        var hover = normal.Clone();
        hover.Name = "hover-test";
        var control = new MiiImageLoaderWithHover
        {
            Width = 100,
            Height = 100,
            Mii = first,
            ImageVariant = normal,
            HoverVariant = hover,
        };
        var window = new Window { Content = control };
        try
        {
            window.Show();
            window.UpdateLayout();
            images.ClearReceivedCalls();
            control.IsHovered = true;
            await images.Received().GetImageAsync(first, hover);
            images.ClearReceivedCalls();
            control.Mii = second;
            await images.Received().GetImageAsync(second, normal);
            await images.Received().GetImageAsync(second, hover);
            await images.DidNotReceive().GetImageAsync(first, Arg.Any<MiiImageSpecifications>());
            var replacement = normal.Clone();
            control.ImageVariant = replacement;
            await images.Received().GetImageAsync(second, replacement);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InteractiveRendering_UsesInjectedRendererOnlyAfterAttachment()
    {
        var renderer = Substitute.For<IMiiNativeRenderer>();
        var rendered = new TaskCompletionSource<Mii>(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer
            .RenderBufferAsync(Arg.Any<Mii>(), Arg.Any<string>(), Arg.Any<MiiImageSpecifications>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                rendered.TrySetResult(call.Arg<Mii>());
                return Task.FromResult((OperationResult<NativeMiiPixelBuffer>)new OperationError { Message = "Rendering unavailable" });
            });
        InstallThemes(renderer);
        var mii = MiiFactory.CreateRandomMii(new Testably.Abstractions.RealRandomSystem().Random.New(1));
        var control = new Mii3DRender
        {
            Width = 100,
            Height = 100,
            Mii = mii,
        };
        Assert.False(rendered.Task.IsCompleted);
        var window = new Window { Content = control };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Same(mii, await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnimatedMiis_OnlyStartTheirLiveViewOnceScrolledIntoView()
    {
        InstallThemes(animate: true);
        var mii = MiiFactory.CreateRandomMii(new RealRandomSystem().Random.New(3));
        var cards = Enumerable
            .Range(0, 12)
            .Select(_ => new MiiAnimatedImage
            {
                Width = 144,
                Height = 144,
                Mii = mii,
                Performance = MiiPerformances.FriendOffline,
            })
            .ToList();
        var panel = new StackPanel();
        foreach (var card in cards)
            panel.Children.Add(card);
        var scroller = new ScrollViewer { Content = panel, Height = 300 };
        var window = new Window
        {
            Content = scroller,
            Width = 300,
            Height = 300,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            static bool IsLive(MiiAnimatedImage card) => card.GetVisualDescendants().OfType<MiiRealtimeView>().Any();
            Assert.True(IsLive(cards[0]));
            Assert.False(IsLive(cards[^1]));
            // Nothing is rendered for cards that wait to be seen (no still pictures either).
            Assert.All(
                cards.Where(card => !IsLive(card)),
                card => Assert.Null(card.GetVisualDescendants().OfType<MiiImageView>().Single().Mii)
            );

            scroller.Offset = new Vector(0, panel.Bounds.Height);
            window.UpdateLayout();
            // Live Miis start one per frame.
            for (var frame = 0; frame < cards.Count; frame++)
            {
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(IsLive(cards[^1]));
            // Scrolled back and forth, a card keeps its live view.
            var live = cards[0].GetVisualDescendants().OfType<MiiRealtimeView>().Single();
            scroller.Offset = default;
            window.UpdateLayout();
            Assert.Same(live, cards[0].GetVisualDescendants().OfType<MiiRealtimeView>().Single());
        }
        finally
        {
            window.Close();
        }
    }

    private static IMiiImagesSingletonService InstallThemes(IMiiNativeRenderer? nativeRenderer = null, bool animate = false)
    {
        var images = Substitute.For<IMiiImagesSingletonService>();
        images
            .GetImageAsync(Arg.Any<Mii>(), Arg.Any<MiiImageSpecifications>())
            .Returns(Task.FromResult((OperationResult<Bitmap>)new OperationError { Message = "No image" }));
        var settings = Substitute.For<ISettingsManager>();
        settings.ENABLE_ANIMATIONS.Returns(new WhWzSetting<bool>("EnableAnimations", animate));
        new MiiControlThemes(
            images,
            Substitute.For<ISeasonalCalendar>(),
            nativeRenderer ?? Substitute.For<IMiiNativeRenderer>(),
            Substitute.For<IMiiAnimationLibrary>(),
            new RealRandomSystem(),
            settings
        ).Install(Application.Current!.Resources);
        return images;
    }
}
