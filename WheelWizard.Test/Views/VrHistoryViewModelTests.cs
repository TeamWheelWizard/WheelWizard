using System.Linq.Expressions;
using WheelWizard.RrRooms;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Patterns;

namespace WheelWizard.Test.Views;

public class VrHistoryViewModelTests
{
    [Fact]
    public async Task ChangingRange_PreservesTheNewestResponse()
    {
        var api = Substitute.For<IApiCaller<IRwfcApi>>();
        var older = new TaskCompletionSource<OperationResult<RwfcPlayerVrHistoryResponse>>();
        var newer = new TaskCompletionSource<OperationResult<RwfcPlayerVrHistoryResponse>>();
        api.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>()).Returns(older.Task, newer.Task);
        using var model = new VrHistoryViewModel(api);

        var first = model.LoadAsync("123456789012", 30);
        var second = model.LoadAsync("123456789012", 7);
        var history = new RwfcPlayerVrHistoryResponse();
        newer.SetResult(Ok(history));
        await second;
        older.SetResult(Ok(new RwfcPlayerVrHistoryResponse()));
        await first;

        Assert.Same(history, model.History);
        Assert.Equal(7, model.HistoryDays);
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task MissingFriendCode_InvalidatesAnEarlierRequestWithoutAnotherApiCall()
    {
        var api = Substitute.For<IApiCaller<IRwfcApi>>();
        var pending = new TaskCompletionSource<OperationResult<RwfcPlayerVrHistoryResponse>>();
        api.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>()).Returns(pending.Task);
        using var model = new VrHistoryViewModel(api);

        var first = model.LoadAsync("1234-5678-9012", 30);
        await model.LoadAsync("0000-0000-0000", 30);
        pending.SetResult(Ok(new RwfcPlayerVrHistoryResponse()));
        await first;

        Assert.False(model.HasFriendCode);
        Assert.False(model.IsLoading);
        Assert.Null(model.History);
        await api.Received(1).CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>());
    }

    [Fact]
    public async Task Disposal_CancelsWaitingAndIgnoresLateResults()
    {
        var api = Substitute.For<IApiCaller<IRwfcApi>>();
        var pending = new TaskCompletionSource<OperationResult<RwfcPlayerVrHistoryResponse>>();
        api.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>()).Returns(pending.Task);
        var model = new VrHistoryViewModel(api);
        var load = model.LoadAsync("1234-5678-9012", 30);

        model.Dispose();
        await load;
        pending.SetResult(Ok(new RwfcPlayerVrHistoryResponse()));
        await model.LoadAsync("1234-5678-9012", 30);

        Assert.Null(model.History);
        Assert.False(model.IsLoading);
        await api.Received(1).CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>());
    }

    [Fact]
    public async Task Request_NormalizesFriendCodeAndReportsFailure()
    {
        var api = Substitute.For<IApiCaller<IRwfcApi>>();
        var endpoint = Substitute.For<IRwfcApi>();
        api.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>())
            .Returns(call =>
            {
                _ = call.Arg<Expression<Func<IRwfcApi, Task<RwfcPlayerVrHistoryResponse>>>>().Compile()(endpoint);
                return Task.FromResult((OperationResult<RwfcPlayerVrHistoryResponse>)new OperationError { Message = "Unavailable" });
            });
        using var model = new VrHistoryViewModel(api);

        await model.LoadAsync(" 123456789012 ", 999);

        await endpoint.Received(1).GetPlayerVrHistoryAsync("1234-5678-9012", 999);
        Assert.Equal("Unavailable", model.Error?.Message);
        Assert.False(model.IsLoading);
    }
}
