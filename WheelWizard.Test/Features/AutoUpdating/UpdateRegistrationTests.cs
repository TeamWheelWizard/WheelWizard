using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Testably.Abstractions.Testing;
using WheelWizard.AutoUpdating;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.AutoUpdating;

public class UpdateRegistrationTests
{
    [Fact]
    public void ReleaseContract_DeserializesPublicationDate()
    {
        var github = JsonSerializer.Deserialize<GithubRelease>(
            """{"tag_name":"v2.0.0","published_at":"2026-10-11T12:00:00Z"}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero), github!.PublishedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_PreservesBuildTargetAndProtectsFlatpakExecutable(bool flatpak)
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(true);
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        if (flatpak)
        {
            environment.GetEnvironmentVariable("FLATPAK_ID").Returns("io.github.TeamWheelWizard.WheelWizard");
            fs.File.WriteAllText("/.flatpak-info", "");
        }
        var services = new ServiceCollection();
        services.AddWheelWizardServices();
        services.AddSingleton<IFileSystem>(fs);
        services.AddSingleton(environment);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var platform = provider.GetRequiredService<IUpdatePlatform>();
        if (flatpak)
        {
            Assert.IsType<FallbackUpdatePlatform>(platform);
            Assert.False(platform.SupportsAutomaticUpdate);
            return;
        }

#if LINUX
        Assert.IsType<LinuxUpdatePlatform>(platform);
#elif WINDOWS
        Assert.IsType<WindowsUpdatePlatform>(platform);
#else
        Assert.IsType<FallbackUpdatePlatform>(platform);
#endif
    }
}
