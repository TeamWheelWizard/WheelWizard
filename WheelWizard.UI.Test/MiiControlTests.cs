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
    public async Task EditorName_HeaderAndInfoMirrorEditsAndUndo()
    {
        InstallThemes();
        var settings = Substitute.For<ISettingsManager>();
        settings.ENABLE_ANIMATIONS.Returns(new WhWzSetting<bool>("EnableAnimations", false));
        var page = new WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorPage(
            Substitute.For<WheelWizard.Views.Shell.Navigation.INavigationService>(),
            Substitute.For<IMiiDbService>(),
            settings,
            Substitute.For<IMiiNativeRenderer>(),
            Substitute.For<IMiiAnimationLibrary>(),
            Substitute.For<ISeasonalCalendar>(),
            new RealRandomSystem(),
            new WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorRequest(MiiFactory.CreateDefaultMale())
        );
        var header = page.FindControl<WheelWizard.Views.Components.InputField>("TitleNameField")!;
        var info = page.FindControl<WheelWizard.Views.Components.InputField>("NameField")!;
        Assert.Equal(info.Text, header.Text);
        header.Text = "Header";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal("Header", info.Text);
        info.Text = "Info";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal("Info", header.Text);
        header.Text = "X";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal("X", info.Text);
        Assert.True(info.HasError);
        Assert.Equal(string.Empty, header.ErrorText);
        Assert.True(header.HasError);
        page.FindControl<WheelWizard.Views.Components.ActionButton>("UndoButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal(info.Text, header.Text);
        Assert.NotEqual("X", header.Text);
        var window = new Window
        {
            Content = page,
            Width = 572,
            Height = 700,
        };
        try
        {
            window.Show();
            header.Text = "Short";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            var shortWidth = header.Bounds.Width;
            header.Text = "LongerName";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            Assert.True(header.Bounds.Width > shortWidth, $"Short: {shortWidth}; long: {header.Bounds.Width}");
            Assert.True(header.Bounds.Right < page.FindControl<StackPanel>("TopActions")!.Bounds.Left);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Podium_WithoutAnimations_CentersStillsOnTheirSteps()
    {
        InstallThemes();
        var renderer = Substitute.For<IMiiNativeRenderer>();
        var identity = System.Numerics.Matrix4x4.Identity;
        renderer
            .GetRealtimeFrameSetup(Arg.Any<string>(), Arg.Any<MiiImageSpecifications>(), Arg.Any<float>())
            .Returns(
                new MiiRealtimeFrameSetup(
                    false,
                    true,
                    default,
                    identity,
                    identity,
                    identity,
                    System.Numerics.Matrix4x4.CreateScale(0.005f, 0.005f, 1),
                    default,
                    default,
                    default,
                    default,
                    default
                )
            );
        var stage = new WheelWizard.WheelWizardData.Views.LeaderboardPodiumStage
        {
            First = MiiFactory.CreateDefaultMale(),
            Second = MiiFactory.CreateDefaultMale(),
            Third = MiiFactory.CreateDefaultMale(),
        };
        var steps = Enumerable
            .Range(1, 3)
            .Select(place =>
            {
                var step = new WheelWizard.WheelWizardData.Views.LeaderboardPodiumStep();
                WheelWizard.WheelWizardData.Views.LeaderboardPodiumStage.SetPlace(step, place);
                stage.Children.Add(step);
                return step;
            })
            .ToArray();
        stage.Initialize(
            renderer,
            Substitute.For<IMiiAnimationLibrary>(),
            new RealRandomSystem().Random.Shared,
            Substitute.For<ISeasonalCalendar>(),
            false
        );
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var window = new Window
        {
            Content = stage,
            Width = 440,
            Height = 380,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var stills = stage.Children.OfType<MiiImageLoader>().ToArray();
            Assert.Equal(3, stills.Length);
            foreach (var (still, step) in stills.Zip(steps.Reverse()))
            {
                Assert.InRange(Math.Abs(still.Bounds.Center.X - step.Bounds.Center.X), 0, 1);
                Assert.InRange(Math.Abs(still.Bounds.Y + still.Bounds.Height * 0.975 - step.Bounds.Y), 0, 1);
                Assert.Equal(still.Bounds.Width, still.Bounds.Height);
                Assert.True(still.DesiredSize.Width < stage.Bounds.Height);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Podium_WithoutMiis_KeepsItsLightsOff()
    {
        var stage = new WheelWizard.WheelWizardData.Views.LeaderboardPodiumStage();
        for (var place = 1; place <= 3; place++)
        {
            var step = new WheelWizard.WheelWizardData.Views.LeaderboardPodiumStep();
            WheelWizard.WheelWizardData.Views.LeaderboardPodiumStage.SetPlace(step, place);
            stage.Children.Add(step);
        }
        stage.Initialize(
            Substitute.For<IMiiNativeRenderer>(),
            Substitute.For<IMiiAnimationLibrary>(),
            new RealRandomSystem().Random.Shared,
            Substitute.For<ISeasonalCalendar>(),
            false
        );
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.All(stage.Children, step => Assert.DoesNotContain("Lit", step.Classes));
    }

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
