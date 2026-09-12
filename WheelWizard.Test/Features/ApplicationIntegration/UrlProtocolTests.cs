using WheelWizard.ApplicationIntegration;
using WheelWizard.GameBanana.InstallRequests;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.ApplicationIntegration;

public class UrlProtocolTests
{
    [Theory]
    [InlineData("wheelwizard://123", null)]
    [InlineData("WHEELWIZARD://123/", null)]
    [InlineData("wheelwizard://123,https://example.com/file.zip", "https://example.com/file.zip")]
    [InlineData("wheelwizard://123,https://example.com/a,b/?key=one%2Ctwo", "https://example.com/a,b/?key=one%2Ctwo")]
    public void Parse_PreservesIdAndCompleteDownloadUrl(string url, string? downloadUrl)
    {
        var result = ModInstallRequest.Parse(url);
        Assert.True(result.IsSuccess);
        Assert.Equal(new ModInstallRequest(123, downloadUrl), result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://123")]
    [InlineData("wheelwizard://")]
    [InlineData("wheelwizard://-1")]
    [InlineData("wheelwizard://0")]
    [InlineData("wheelwizard://2147483648")]
    [InlineData("wheelwizard://123,relative.zip")]
    [InlineData("wheelwizard://123,file:///tmp/mod.zip")]
    public async Task InvalidRequest_ShowsAnErrorWithoutOpeningModDetails(string url)
    {
        var presentation = Substitute.For<IModInstallRequestPresentation>();
        var handler = new ModInstallRequestHandler(presentation);

        Assert.True((await handler.HandleAsync(url)).IsFailure);

        presentation.Received(1).ShowError(Arg.Any<string>());
        await presentation.DidNotReceive().ShowModAsync(Arg.Any<ModInstallRequest>());
    }

    [Fact]
    public async Task ValidRequest_OpensDetails_AndLoadFailuresAreReturnedAndDisplayed()
    {
        var presentation = Substitute.For<IModInstallRequestPresentation>();
        var handler = new ModInstallRequestHandler(presentation);
        Assert.True((await handler.HandleAsync("wheelwizard://123")).IsSuccess);
        await presentation.Received(1).ShowModAsync(new(123, null));
        presentation.ShowModAsync(Arg.Any<ModInstallRequest>()).Returns(Task.FromException(new IOException("download unavailable")));

        Assert.True((await handler.HandleAsync("wheelwizard://123")).IsFailure);
        presentation.Received(1).ShowError("download unavailable");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("corrupt registration", true)]
    [InlineData("\"C:\\Old\\WheelWizard.exe\" \"%1\"", true)]
    [InlineData("\"C:\\New Folder\\WheelWizard.exe\" \"%1\"", false)]
    public void Registration_RepairsMissingStaleOrIncompleteRecords(string? command, bool marker)
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(true);
        var store = Substitute.For<IUrlProtocolRegistrationStore>();
        store.Read("wheelwizard").Returns(new UrlProtocolRegistrationState(command, marker));

        Assert.True(new UrlProtocolRegistration(store, environment).EnsureRegistered(@"C:\New Folder\WheelWizard.exe").IsSuccess);

        store.Received(1).Write("wheelwizard", "\"C:\\New Folder\\WheelWizard.exe\" \"%1\"");
    }

    [Fact]
    public void Registration_IsIdempotentAndDoesNothingOnOtherPlatforms()
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(true);
        var store = Substitute.For<IUrlProtocolRegistrationStore>();
        store.Read("wheelwizard").Returns(new UrlProtocolRegistrationState("\"c:\\APP.exe\" \"%1\"", true));
        var registration = new UrlProtocolRegistration(store, environment);
        Assert.True(registration.EnsureRegistered(@"C:\app.exe").IsSuccess);
        store.DidNotReceiveWithAnyArgs().Write(default!, default!);
        store.ClearReceivedCalls();
        environment.IsWindows.Returns(false);
        Assert.True(registration.EnsureRegistered(null).IsSuccess);
        Assert.Empty(store.ReceivedCalls());
    }

    [Fact]
    public void Registration_ReturnsPersistenceErrorsWithoutThrowingDuringStartup()
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(true);
        var store = Substitute.For<IUrlProtocolRegistrationStore>();
        store.When(value => value.Write(Arg.Any<string>(), Arg.Any<string>())).Do(_ => throw new UnauthorizedAccessException());

        var result = new UrlProtocolRegistration(store, environment).EnsureRegistered(@"C:\app.exe");

        Assert.True(result.IsFailure);
        Assert.IsType<UnauthorizedAccessException>(result.Error.Exception);
    }
}
