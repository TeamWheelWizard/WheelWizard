using System.Linq.Expressions;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Shared.Services;

namespace WheelWizard.Test.Features
{
    public class GameBananaTests
    {
        private readonly IApiCaller<IGameBananaApi> _apiCaller;
        private readonly IGameBananaSingletonService _service;
        private readonly IGameBananaApi _api;

        public GameBananaTests()
        {
            _api = Substitute.For<IGameBananaApi>();
            _apiCaller = Substitute.For<IApiCaller<IGameBananaApi>>();
            _service = new GameBananaSingletonService(_apiCaller);
        }

        [Fact]
        public async Task GetModSearchResults_WithValidSearchTerm_ReturnsResults()
        {
            // Arrange
            var searchTerm = "TestMod";
            var page = 1;
            var expectedResults = new GameBananaSearchResults()
            {
                Records = [CreateFakeModPreview(1), CreateFakeModPreview(2), CreateFakeModPreview(3)],
                MetaData = new()
                {
                    RecordCount = 3,
                    PerPage = 15,
                    IsComplete = true,
                },
            };

            _apiCaller
                .CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaSearchResults>>>>())
                .Returns(Ok(expectedResults));

            // Act
            var result = await _service.GetModSearchResults(searchTerm, page);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(3, result.Value.Records.Count);
            Assert.Equal(3, result.Value.MetaData.RecordCount);

            await _apiCaller.Received(1).CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaSearchResults>>>>());
        }

        [Fact]
        public async Task GetModSearchResults_WithEmptySearchTerm_UsesDefaultTerm()
        {
            // Arrange
            var emptySearchTerm = "";
            var page = 1;
            var expectedResults = new GameBananaSearchResults()
            {
                Records = [CreateFakeModPreview(1)],
                MetaData = new()
                {
                    RecordCount = 1,
                    PerPage = 15,
                    IsComplete = true,
                },
            };

            _apiCaller
                .CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaSearchResults>>>>())
                .Returns(Ok(expectedResults));

            // Act
            var result = await _service.GetModSearchResults(emptySearchTerm, page);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Single(result.Value.Records);
        }

        [Fact]
        public async Task GetModSearchResults_WithApiError_ReturnsFailure()
        {
            // Arrange
            var searchTerm = "TestMod";
            var page = 1;
            var expectedError = "API Error";

            _apiCaller
                .CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaSearchResults>>>>())
                .Returns(Fail(expectedError));

            // Act
            var result = await _service.GetModSearchResults(searchTerm, page);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(expectedError, result.Error.Message);
        }

        [Fact]
        public async Task GetModDetails_WithValidId_ReturnsDetails()
        {
            // Arrange
            var modId = 123;
            var expectedDetails = CreateFakeModDetails(modId);

            _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaModDetails>>>>()).Returns(Ok(expectedDetails));

            // Act
            var result = await _service.GetModDetails(modId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(modId, result.Value.Id);
            Assert.Equal("Test Mod 123", result.Value.Name);
            Assert.Equal("1.0", result.Value.Version);
            Assert.Equal("Test Author", result.Value.Author.Name);

            await _apiCaller.Received(1).CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaModDetails>>>>());
        }

        [Fact]
        public async Task GetModDetails_WithApiError_ReturnsFailure()
        {
            // Arrange
            var modId = 123;
            var expectedError = "API Error";

            _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaModDetails>>>>()).Returns(Fail(expectedError));

            // Act
            var result = await _service.GetModDetails(modId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(expectedError, result.Error.Message);
        }

        [Fact]
        public void GetLoadingPreview_ReturnsValidLoadingPreview()
        {
            // Act
            var result = _service.GetLoadingPreview();

            // Assert
            Assert.Equal("LOADING", result.Name);
            Assert.Equal("LOADING", result.Author.Name);
            // The only 2 important properties are Name and Author.Name
            // For all the other properties the values all don't matter
            // These 2 properties are used in the ModBrowserListItem component, so if you want to change this, you will
            // also have to change it there
        }

        [Fact]
        public async Task GetModSearchResults_WithPagination_PassesCorrectPageNumber()
        {
            // Arrange
            var searchTerm = "TestMod";
            var page = 3;
            var expectedResults = new GameBananaSearchResults()
            {
                Records = [CreateFakeModPreview(7), CreateFakeModPreview(8), CreateFakeModPreview(9)],
                MetaData = new()
                {
                    RecordCount = 3,
                    PerPage = 15,
                    IsComplete = true,
                },
            };

            _apiCaller
                .CallApiAsync(Arg.Any<Expression<Func<IGameBananaApi, Task<GameBananaSearchResults>>>>())
                .Returns(Ok(expectedResults));

            // Act
            var result = await _service.GetModSearchResults(searchTerm, page);

            // Assert
            Assert.True(result.IsSuccess);
        }

        private GameBananaModPreview CreateFakeModPreview(int id)
        {
            return new()
            {
                Id = id,
                Name = $"Test Mod {id}",
                Tags = [],
                Version = "",
                Author = new()
                {
                    Name = "Test Author",
                    ProfileUrl = "",
                    AvatarUrl = "",
                },
                ProfileUrl = "",
                DateAdded = 0,
                DateModified = 0,
                Game = new()
                {
                    Name = "",
                    ProfileUrl = "",
                    IconUrl = "",
                },
                RootCategory = new()
                {
                    Name = "",
                    ProfileUrl = "",
                    IconUrl = "",
                },
                ModelName = "Mod",
                PreviewMedia = new(),
            };
        }

        private GameBananaModDetails CreateFakeModDetails(int id)
        {
            return new()
            {
                Id = id,
                Name = $"Test Mod {id}",
                Version = "1.0",
                ProfileUrl = $"https://gamebanana.com/mods/{id}",
                LikeCount = 100,
                ViewCount = 1000,
                DateAdded = 1609459200, // 2021-01-01
                DateModified = 1609545600, // 2021-01-02
                IsObsolete = false,
                Author = new()
                {
                    Name = "Test Author",
                    ProfileUrl = "https://gamebanana.com/members/123",
                    AvatarUrl = "https://gamebanana.com/avatar.jpg",
                },
                Game = new()
                {
                    Name = "Test Game",
                    ProfileUrl = "https://gamebanana.com/games/123",
                    IconUrl = "https://gamebanana.com/game.jpg",
                },
                Category = new()
                {
                    Name = "Test Category",
                    ProfileUrl = "https://gamebanana.com/categories/123",
                    IconUrl = "https://gamebanana.com/category.jpg",
                },
                SuperCategory = null,
                Text = "This is a test mod description",
                License = "MIT",
                LicenseAllowance = null,
                DownloadCount = 500,
                Files =
                [
                    new()
                    {
                        FileName = "test-mod.zip",
                        FileSize = 1024,
                        DownloadUrl = $"https://gamebanana.com/dl/{id}",
                    },
                ],
            };
        }
    }
}
