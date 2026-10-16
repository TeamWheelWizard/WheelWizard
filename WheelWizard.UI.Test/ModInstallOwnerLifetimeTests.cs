using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NSubstitute;
using WheelWizard.GameBanana;
using WheelWizard.Mods;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Shared;
using WheelWizard.Shared.Downloads;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.UI.Test;

public class ModInstallOwnerLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ClosedOwner_CancelsDownloadAndSuppressesLateSuccessOrFailureDialogs(bool downloadFails, bool detachContent)
    {
        var manager = Substitute.For<IModManager>();
        var gameBanana = Substitute.For<IGameBananaSingletonService>();
        var downloads = Substitute.For<IDownloadService>();
        var paths = Substitute.For<IModPaths>();
        var presentation = Substitute.For<IModOperationPresentation>();
        var directory = Path.Combine(Path.GetTempPath(), "wheelwizard-owner-test-" + Guid.NewGuid());
        paths.DownloadFolderPath.Returns(directory);
        var started = new TaskCompletionSource<CancellationToken>();
        var finished = new TaskCompletionSource<OperationResult<string>>();
        downloads
            .DownloadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<DownloadProgress>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                started.SetResult(call.Arg<CancellationToken>());
                // Deliberately return late instead of honoring cancellation, to exercise the race.
                return finished.Task;
            });
        var content = new ModContent(manager, gameBanana, downloads, Substitute.For<IGameBananaMediaService>(), paths, presentation);
        var mod = ModTestFixture.CreateMod(123, "Test mod");
        mod.Files = [new() { FileName = "mod.zip", DownloadUrl = "https://example.com/mod.zip" }];
        typeof(ModContent).GetProperty("CurrentMod", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(content, mod);
        using var owner = new UiTestHost();
        owner.Body.Child = content;
        owner.Show();
        // Keep a second app window open: late failures must not surface there either.
        using var other = new UiTestHost();
        other.Show();
        try
        {
            var install = (Task)
                typeof(ModContent)
                    .GetMethod("InstallCurrentModAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(content, null)!;
            var question = Assert.IsType<YesNoWindow>(owner.Modals.TopModal);
            question.FindControl<ActionButton>("YesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            owner.Close();
            Assert.True(token.IsCancellationRequested);
            Assert.False(owner.IsVisible);
            if (detachContent)
            {
                owner.Body.Child = null;
                Assert.Null(TopLevel.GetTopLevel(content));
            }
            else
                Assert.Same(owner, TopLevel.GetTopLevel(content));
            File.WriteAllText(Path.Combine(directory, "mod.zip"), "Downloaded test file");
            finished.SetResult(
                downloadFails
                    ? (OperationResult<string>)new OperationError { Message = "Late failure" }
                    : OperationResult.Ok(Path.Combine(directory, "mod.zip"))
            );
            await install.WaitAsync(TimeSpan.FromSeconds(2));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.False(owner.Modals.HasModals);
            Assert.False(other.Modals.HasModals);
            Assert.Empty(presentation.ReceivedCalls());
            Assert.Empty(manager.ReceivedCalls());
            Assert.Empty(gameBanana.ReceivedCalls());
        }
        finally
        {
            owner.Close();
            other.Close();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
