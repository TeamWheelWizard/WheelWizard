using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NSubstitute;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
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
        typeof(ModContent).GetProperty("CurrentMod", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(content, CreateMod());
        var owner = new PopupWindow(true, false, false, "Mod viewer");
        owner.FindControl<ContentPresenter>("PopupContent")!.Content = content;
        owner.Show();
        // Keep a second app window open: late failures must not surface there either.
        var other = new PopupWindow(true, true, false, "Other window");
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
                owner.FindControl<ContentPresenter>("PopupContent")!.Content = null;
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

    private static GameBananaModDetails CreateMod() =>
        new()
        {
            Id = 123,
            Name = "Test mod",
            Version = "1",
            ProfileUrl = "https://example.com/mod",
            LikeCount = 0,
            ViewCount = 0,
            DateAdded = 0,
            DateModified = 0,
            IsObsolete = false,
            Author = new GameBananaAuthor { Name = "Author", ProfileUrl = "https://example.com/author" },
            Game = new GameBananaGame
            {
                Name = "Mario Kart Wii",
                ProfileUrl = "https://example.com/game",
                IconUrl = "",
            },
            Category = new GameBananaCategory { Name = "Maps", ProfileUrl = "https://example.com/maps" },
            Text = "Test mod",
            License = "",
            DownloadCount = 0,
            Files = new List<GameBananaModFiles>
            {
                new() { FileName = "mod.zip", DownloadUrl = "https://example.com/mod.zip" },
            },
        };
}
