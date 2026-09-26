using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared;
using WheelWizard.Shared.Calendar;
using WheelWizard.Views.Patterns;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.UI.Test;

public class MiiControlTests
{
    [AvaloniaFact]
    public async Task FriendPortrait_RemainsVisibleWithoutAnExplicitWidth()
    {
        var images = InstallThemes();
        using var bitmap = new WriteableBitmap(new PixelSize(512, 512), new Vector(96, 96));
        var rendered = new TaskCompletionSource<OperationResult<Bitmap>>();
        images.GetImageAsync(Arg.Any<Mii>(), Arg.Any<MiiImageSpecifications>()).Returns(rendered.Task);
        var card = new FriendsListItem
        {
            Width = 428,
            Height = 124,
            Mii = MiiFactory.CreateRandomMii(new Testably.Abstractions.RealRandomSystem().Random.New(1)),
        };
        var window = new Window { Content = card };
        try
        {
            window.Show();
            window.UpdateLayout();
            rendered.SetResult(bitmap);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            foreach (var (online, pending) in new[] { (false, false), (true, false), (false, true) })
            {
                card.IsOnline = online;
                card.IsPending = pending;
                window.UpdateLayout();
                var control = Assert.Single(card.GetVisualDescendants().OfType<MiiImageLoader>());
                var view = Assert.Single(control.GetVisualDescendants().OfType<MiiImageView>());
                Assert.Same(bitmap, Assert.Single(view.GeneratedImages));
                var image = Assert.Single(view.GetVisualDescendants().OfType<Image>(), image => image.IsVisible && image.Source == bitmap);
                var position = image.TranslatePoint(default, card)!.Value;
                var visiblePortrait = new Rect(position, image.Bounds.Size).Intersect(new Rect(card.Bounds.Size));
                Assert.True(
                    visiblePortrait.Width >= 50 && visiblePortrait.Height >= 100,
                    $"Portrait is clipped outside the card: {position}, {image.Bounds}; visible area {visiblePortrait}"
                );
            }
        }
        finally
        {
            window.Close();
        }
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
        var window = new Window { Content = new StackPanel { Children = { normal, hover, interactive } } };
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

    private static IMiiImagesSingletonService InstallThemes(IMiiNativeRenderer? nativeRenderer = null)
    {
        var images = Substitute.For<IMiiImagesSingletonService>();
        images
            .GetImageAsync(Arg.Any<Mii>(), Arg.Any<MiiImageSpecifications>())
            .Returns(Task.FromResult((OperationResult<Bitmap>)new OperationError { Message = "No image" }));
        new MiiControlThemes(images, Substitute.For<ISeasonalCalendar>(), nativeRenderer ?? Substitute.For<IMiiNativeRenderer>()).Install(
            Application.Current!.Resources
        );
        return images;
    }
}
