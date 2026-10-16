using NSubstitute;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Mods;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Shared.Downloads;

namespace WheelWizard.UI.Test;

internal static class ModTestFixture
{
    public static ModContent CreateContent(IGameBananaSingletonService api, IGameBananaMediaService? media = null) =>
        new(
            Substitute.For<IModManager>(),
            api,
            Substitute.For<IDownloadService>(),
            media ?? Substitute.For<IGameBananaMediaService>(),
            Substitute.For<IModPaths>(),
            Substitute.For<IModOperationPresentation>()
        );

    public static GameBananaModDetails CreateMod(int id, string name, int imageCount = 0) =>
        new()
        {
            Id = id,
            Name = name,
            Version = "1.0",
            ProfileUrl = "https://example.com/mod",
            LikeCount = 12345,
            ViewCount = 1234567,
            DownloadCount = 12345,
            DateAdded = 0,
            DateModified = 0,
            IsObsolete = false,
            Author = new() { Name = "Mod author", ProfileUrl = "https://example.com/author" },
            Game = new()
            {
                Name = "Mario Kart Wii",
                ProfileUrl = "https://example.com/game",
                IconUrl = "",
            },
            Category = new() { Name = "Tracks", ProfileUrl = "https://example.com/tracks" },
            Text = string.Concat(
                Enumerable.Repeat("<p>A detailed description with installation instructions and mod information.</p>", 30)
            ),
            License = "",
            PreviewMedia = new()
            {
                Images = Enumerable
                    .Range(1, imageCount)
                    .Select(i => new GameBananaImage
                    {
                        Type = "screenshot",
                        BaseUrl = "https://example.com",
                        File = $"{i}.png",
                    })
                    .ToList(),
            },
        };
}
