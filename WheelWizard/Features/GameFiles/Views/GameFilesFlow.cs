using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WheelWizard.Recomp;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.GameFiles.Views;

/// <summary>
/// The questions around changing the extracted game: what to do with what is there now, waiting for a running game
/// to close, then extracting with progress. Shared by Settings, the game setup page and the onboarding.
/// </summary>
public interface IGameFilesFlow
{
    /// <summary>Makes <paramref name="imagePath"/> the game, asking whatever has to be asked first.</summary>
    /// <param name="progress">Shows progress inline; without it a progress window is shown.</param>
    /// <returns>Whether the game is now extracted from (or remembered as) this image.</returns>
    Task<bool> ChangeImageAsync(
        string imagePath,
        IProgress<GameExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Extracts the image already picked, for someone coming from an older version.</summary>
    Task<bool> ExtractCurrentAsync(IProgress<GameExtractionProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Asks whether to move aside or delete a folder that is in the way.</summary>
    Task<bool> ResolveUnknownFolderAsync();

    /// <summary>Waits until Dolphin and WiiCompiled are closed, offering to close them. False when the user gave up.</summary>
    Task<bool> WaitForGameClosedAsync();

    /// <summary>Points an installed WiiCompiled at the extracted game. Failures leave WiiCompiled asking for an update.</summary>
    Task LinkWiiCompiledAsync();
}

public sealed class GameFilesFlow(IGameFilesService gameFiles, IServiceProvider services, ILogger<GameFilesFlow> logger) : IGameFilesFlow
{
    private const string PalGameIdPrefix = "RMCP";

    public async Task<bool> ChangeImageAsync(
        string imagePath,
        IProgress<GameExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var plan = gameFiles.Plan(imagePath);
        if (plan.IsFailure)
        {
            await ShowErrorAsync(t("message_error.invalid_game_file.title"), plan.Error.Message);
            return false;
        }

        var replace = false;
        switch (plan.Value.Kind)
        {
            case GameChangeKind.SameGame:
                return await RunAsync(imagePath, replaceExisting: false, progress, cancellationToken);
            case GameChangeKind.UnknownFolder:
                if (!await ResolveUnknownFolderAsync())
                    return false;
                break;
            case GameChangeKind.Replace:
                if (!await ConfirmReplaceAsync(plan.Value))
                    return false;
                replace = true;
                break;
        }

        return await RunAsync(imagePath, replace, progress, cancellationToken);
    }

    public async Task<bool> ExtractCurrentAsync(
        IProgress<GameExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var image = gameFiles.ImagePath;
        return image is not null && await ChangeImageAsync(image, progress, cancellationToken);
    }

    private async Task<bool> ConfirmReplaceAsync(GameChangePlan plan)
    {
        var current = plan.Current!;
        var body = t(
            "game_files.replace_body",
            new { current = Describe(current.GameId, current.Revision), incoming = Describe(plan.Image.GameId, plan.Image.Revision) }
        );
        var recomp = services.GetService<IRecompInstallService>();
        if (recomp?.IsInstalled == true && !plan.Image.GameId.StartsWith(PalGameIdPrefix, StringComparison.OrdinalIgnoreCase))
            body += "\n\n" + t("game_files.replace_breaks_wiicompiled");

        return await new YesNoWindow()
            .SetMainText(t("game_files.replace_title"))
            .SetExtraText(body)
            .SetButtonText(t("game_files.replace_action"), t("action.cancel"))
            .SetButtonTones(ActionButtonTone.Danger, ActionButtonTone.Secondary)
            .AwaitAnswer();
    }

    private static string Describe(string gameId, byte revision)
    {
        var region = GameRegion.GetDisplayName(GameRegion.FromGameId(gameId));
        return revision == 0 ? $"Mario Kart Wii · {region} ({gameId})" : $"Mario Kart Wii · {region} ({gameId}, rev {revision})";
    }

    public async Task<bool> ResolveUnknownFolderAsync()
    {
        var moveAside = await new YesNoWindow()
            .SetMainText(t("game_files.unknown_folder_title"))
            .SetExtraText(t("game_files.unknown_folder_body"))
            .SetButtonText(t("game_files.move_aside"), t("game_files.delete"))
            .SetButtonTones(ActionButtonTone.Brand, ActionButtonTone.Secondary)
            .AwaitWithChoiceAsync();

        switch (moveAside)
        {
            case true:
            {
                var moved = await Task.Run(gameFiles.MoveUnknownAside);
                if (moved.IsSuccess)
                    return true;
                await ShowErrorAsync(t("game_files.unknown_folder_title"), moved.Error.Message);
                return false;
            }
            case false:
            {
                var sure = await new YesNoWindow()
                    .SetMainText(t("game_files.delete_confirm_title"))
                    .SetExtraText(t("game_files.delete_confirm_body"))
                    .SetButtonText(t("game_files.delete"), t("action.cancel"))
                    .SetButtonTones(ActionButtonTone.Danger, ActionButtonTone.Secondary)
                    .AwaitAnswer();
                if (!sure)
                    return false;
                var deleted = await Task.Run(gameFiles.DeleteUnknown);
                if (deleted.IsSuccess)
                    return true;
                await ShowErrorAsync(t("game_files.unknown_folder_title"), deleted.Error.Message);
                return false;
            }
            default:
                return false;
        }
    }

    public async Task<bool> WaitForGameClosedAsync()
    {
        if (!await Task.Run(gameFiles.IsGameRunning))
            return true;

        var window = new YesNoWindow()
            .SetMainText(t("game_files.close_game_title"))
            .SetExtraText(t("game_files.close_game_body"))
            .SetButtonText(t("game_files.close_game_action"), t("action.cancel"))
            .SetButtonTones(ActionButtonTone.Secondary, ActionButtonTone.Secondary);

        // The prompt goes away by itself once the game is closed, however that happened.
        var closedByItself = false;
        using var polling = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!polling.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), polling.Token).ConfigureAwait(false);
                if (gameFiles.IsGameRunning())
                    continue;
                closedByItself = true;
                await Dispatcher.UIThread.InvokeAsync(window.Close);
                return;
            }
        });

        var closeForMe = await window.AwaitAnswer();
        await polling.CancelAsync();
        if (closedByItself)
            return true;
        if (!closeForMe)
            return false;

        await Task.Run(gameFiles.CloseRunningGame);
        for (var attempt = 0; attempt < 20 && await Task.Run(gameFiles.IsGameRunning); attempt++)
            await Task.Delay(250);
        return !await Task.Run(gameFiles.IsGameRunning) || await WaitForGameClosedAsync();
    }

    private async Task<bool> RunAsync(
        string imagePath,
        bool replaceExisting,
        IProgress<GameExtractionProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        if (!await WaitForGameClosedAsync())
            return false;

        OperationResult result;
        ProgressWindow? window = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            if (progress is null)
            {
                window = new ProgressWindow(t("game_files.extracting_title"))
                    .SetGoal(t("game_files.extracting_title"))
                    .SetExtraText(t("game_files.extracting"))
                    .SetCancellationTokenSource(cancellation);
                window.Show();
                var shown = window;
                progress = new Progress<GameExtractionProgress>(value =>
                {
                    shown.UpdateProgress((int)(value.Fraction * 100));
                    shown.SetExtraText(value.Message);
                });
            }

            result = await gameFiles.ExtractAsync(imagePath, replaceExisting, progress, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            window?.Close();
        }

        if (result.IsFailure)
        {
            await ShowErrorAsync(t("game_files.extract_failed_title"), result.Error.Message);
            return false;
        }

        await LinkWiiCompiledAsync();
        return true;
    }

    public async Task LinkWiiCompiledAsync()
    {
        var recomp = services.GetService<IRecompInstallService>();
        if (recomp is not { IsInstalled: true })
            return;

        var window = new ProgressWindow(t("game_files.linking_wiicompiled")).SetIndeterminate();
        try
        {
            window.Show();
            var result = await recomp.LinkGameDataAsync();
            if (result.IsFailure)
                logger.LogWarning("WiiCompiled could not be pointed at the extracted game: {Message}", result.Error.Message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "WiiCompiled could not be pointed at the extracted game");
        }
        finally
        {
            window.Close();
        }
    }

    private static Task ShowErrorAsync(string title, string message) =>
        new MessageBoxWindow().SetMessageType(MessageBoxWindow.MessageType.Error).SetTitleText(title).SetInfoText(message).ShowDialog();
}
