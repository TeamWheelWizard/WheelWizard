using System.Linq.Expressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.Models;
using WheelWizard.RrRooms;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;

namespace WheelWizard.Test.Features.RrRooms;

public class RrLeaderboardTests
{
    private const string FriendCode = "3137-3137-3137";

    private readonly IApiCaller<IRwfcApi> _apiCaller = Substitute.For<IApiCaller<IRwfcApi>>();
    private readonly RrLeaderboardSingletonService _service;

    public RrLeaderboardTests()
    {
        _service = new(_apiCaller, new MemoryCache(new MemoryCacheOptions()), NullLogger<RrLeaderboardSingletonService>.Instance);
    }

    private Task<OperationResult<PlayerProfileResponse>> ReceivedPlayerCalls(int count) =>
        _apiCaller.Received(count).CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>());

    [Fact]
    public async Task GetPlayerAsync_FetchesPlayerOnce()
    {
        _apiCaller
            .CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>())
            .Returns(Ok(new PlayerProfileResponse { FriendCode = FriendCode, Vr = 29_367 }));

        var first = await _service.GetPlayerAsync(FriendCode);
        var second = await _service.GetPlayerAsync(FriendCode);

        Assert.Equal(29_367, first.Value.Vr);
        Assert.Equal(29_367, second.Value.Vr);
        await ReceivedPlayerCalls(1);
    }

    [Fact]
    public async Task GetPlayerAsync_DoesNotRetryFailure()
    {
        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>()).Returns(Fail("Not found"));

        Assert.True((await _service.GetPlayerAsync(FriendCode)).IsFailure);
        Assert.True((await _service.GetPlayerAsync(FriendCode)).IsFailure);
        await ReceivedPlayerCalls(1);
    }

    [Fact]
    public async Task GetPlayerAsync_SharesRequestStillLoading()
    {
        var response = new TaskCompletionSource<OperationResult<PlayerProfileResponse>>();
        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>()).Returns(response.Task);

        var first = _service.GetPlayerAsync(FriendCode);
        var second = _service.GetPlayerAsync(FriendCode);
        response.SetResult(Ok(new PlayerProfileResponse { Vr = 29_367 }));

        Assert.Equal(29_367, (await first).Value.Vr);
        Assert.Equal(29_367, (await second).Value.Vr);
        await ReceivedPlayerCalls(1);
    }
}
