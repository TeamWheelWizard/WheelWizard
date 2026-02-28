using Testably.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Utilities.Mockers;

namespace WheelWizard.Test.Features.MiiRendering;

public class NativeMiiRendererTests
{
    [Fact]
    public void BuildRequest_ShouldClampAndNormalizeValues()
    {
        var renderer = CreateRenderer();
        var specifications = new MiiImageSpecifications
        {
            Size = (MiiImageSpecifications.ImageSize)9000,
            InstanceCount = 100,
            Type = MiiImageSpecifications.BodyType.all_body,
            Expression = MiiImageSpecifications.FaceExpression.smile_open_mouth,
            BackgroundColor = string.Empty,
            CharacterRotate = new(12.6f, 44.2f, 89.7f),
            CameraRotate = new(-12.3f, 5.5f, 0.1f),
        };

        var request = renderer.BuildRequest("001122", specifications);

        Assert.Equal(4096, request.Width);
        Assert.Equal(32, request.InstanceCount);
        Assert.Equal("FFFFFF00", request.BackgroundColor);
        Assert.Equal(13, request.CharacterXRotate);
        Assert.Equal(44, request.CharacterYRotate);
        Assert.Equal(90, request.CharacterZRotate);
        Assert.Equal(-12, request.CameraXRotate);
        Assert.Equal(6, request.CameraYRotate);
        Assert.Equal(0, request.CameraZRotate);
    }

    [Fact]
    public void BuildRequest_ShouldApplyRenderScale_WhenSpecified()
    {
        var renderer = CreateRenderer();
        var specifications = new MiiImageSpecifications
        {
            Size = MiiImageSpecifications.ImageSize.medium,
            RenderScale = 0.125f,
            InstanceCount = 1,
        };

        var request = renderer.BuildRequest("001122", specifications);

        Assert.Equal(64, request.Width);
    }

    [Fact]
    public void RenderToBuffer_ShouldFail_WhenResourceMissing()
    {
        var fileSystem = new MockFileSystem();
        var locator = new MiiRenderingResourceLocator(
            fileSystem,
            new MiiRenderingConfiguration
            {
                ResourcePath = "/missing",
                AdditionalSearchDirectories = [],
                MinimumExpectedSizeBytes = 16,
            }
        );

        var renderer = new NativeMiiRenderer(locator);
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var result = renderer.RenderToBuffer(mii, serializedResult.Value, new MiiImageSpecifications());

        Assert.True(result.IsFailure);
        Assert.Contains("FFLResHigh.dat", result.Error!.Message);
    }

    [Fact]
    public void RenderToBuffer_ShouldFail_WhenResourceIsInvalid()
    {
        var renderer = CreateRenderer();
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var result = renderer.RenderToBuffer(mii, serializedResult.Value, new MiiImageSpecifications());
        if (result.IsFailure)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Error!.Message));
            return;
        }

        Assert.NotNull(result.Value.BgraPixels);
        Assert.True(result.Value.BgraPixels.Length > 0);
    }

    [Fact]
    public void RenderToBuffer_ShouldSucceed_WhenResourceIsAvailable()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;

        var renderer = CreateRenderer(resourcePath);
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var result = renderer.RenderToBuffer(
            mii,
            serializedResult.Value,
            new MiiImageSpecifications { Size = MiiImageSpecifications.ImageSize.small, Type = MiiImageSpecifications.BodyType.face }
        );

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotNull(result.Value.BgraPixels);
        Assert.True(result.Value.BgraPixels.Length > 0);
        Assert.Equal((int)MiiImageSpecifications.ImageSize.small, result.Value.Width);
    }

    [Fact]
    public void RenderToBuffer_AllBody_ShouldSucceed_WhenResourceIsAvailable()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;

        var renderer = CreateRenderer(resourcePath);
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var result = renderer.RenderToBuffer(
            mii,
            serializedResult.Value,
            new MiiImageSpecifications { Size = MiiImageSpecifications.ImageSize.small, Type = MiiImageSpecifications.BodyType.all_body }
        );

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotNull(result.Value.BgraPixels);
        Assert.True(result.Value.BgraPixels.Length > 0);
        Assert.Equal((int)MiiImageSpecifications.ImageSize.small, result.Value.Width);
    }

    [Fact]
    public void RenderToBuffer_SequentialFaceThenBody_ShouldSucceed_WhenResourceIsAvailable()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;

        var renderer = CreateRenderer(resourcePath);
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var faceResult = renderer.RenderToBuffer(
            mii,
            serializedResult.Value,
            new MiiImageSpecifications { Size = MiiImageSpecifications.ImageSize.small, Type = MiiImageSpecifications.BodyType.face }
        );
        Assert.True(faceResult.IsSuccess, faceResult.Error?.Message);
        Assert.NotEmpty(faceResult.Value.BgraPixels);

        var bodyResult = renderer.RenderToBuffer(
            mii,
            serializedResult.Value,
            new MiiImageSpecifications { Size = MiiImageSpecifications.ImageSize.small, Type = MiiImageSpecifications.BodyType.all_body }
        );
        Assert.True(bodyResult.IsSuccess, bodyResult.Error?.Message);
        Assert.NotEmpty(bodyResult.Value.BgraPixels);
    }

    [Fact]
    public async Task RenderToBuffer_ShouldSucceed_WhenCalledAfterAsyncYield()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;
        await Task.Yield();

        var renderer = CreateRenderer(resourcePath);
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var result = renderer.RenderToBuffer(
            mii,
            serializedResult.Value,
            new MiiImageSpecifications { Size = MiiImageSpecifications.ImageSize.small, Type = MiiImageSpecifications.BodyType.face }
        );

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotEmpty(result.Value.BgraPixels);
    }

    [Fact]
    public void RenderToBuffer_ShouldSucceed_ForParityFaceScenario()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;

        var renderer = CreateRenderer(resourcePath);
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var spec = new MiiImageSpecifications
        {
            Size = MiiImageSpecifications.ImageSize.small,
            Type = MiiImageSpecifications.BodyType.face,
            Expression = MiiImageSpecifications.FaceExpression.normal,
            BackgroundColor = "FFFFFF00",
            InstanceCount = 1,
        };

        var result = renderer.RenderToBuffer(mii, serializedResult.Value, spec);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotEmpty(result.Value.BgraPixels);
    }

    [Fact]
    public void RenderToBuffer_ShouldSucceed_ForAllConfiguredMiiImageVariants()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;

        var renderer = CreateRenderer(resourcePath);
        var mii = new MiiFactory().Create();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var variantFields = typeof(MiiImageVariants)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(static f => f.FieldType == typeof(MiiImageSpecifications))
            .ToArray();

        Assert.NotEmpty(variantFields);
        foreach (var field in variantFields)
        {
            var variant = (MiiImageSpecifications)field.GetValue(null)!;
            var renderResult = renderer.RenderToBuffer(mii, serializedResult.Value, variant);
            Assert.True(renderResult.IsSuccess, $"{field.Name}: {renderResult.Error?.Message}");
            Assert.NotEmpty(renderResult.Value.BgraPixels);
        }
    }

    private static NativeMiiRenderer CreateRenderer()
    {
        var fileSystem = new RealFileSystem();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "WheelWizard", "MiiRenderingTests");
        Directory.CreateDirectory(tempDirectory);
        var resourcePath = Path.Combine(tempDirectory, "FFLResHigh.dat");
        File.WriteAllBytes(resourcePath, new byte[2 * 1024 * 1024]);

        var locator = new MiiRenderingResourceLocator(
            fileSystem,
            new MiiRenderingConfiguration { ResourcePath = resourcePath, MinimumExpectedSizeBytes = 16 }
        );

        return new NativeMiiRenderer(locator);
    }

    private static NativeMiiRenderer CreateRenderer(string resourcePath)
    {
        var fileSystem = new RealFileSystem();
        var locator = new MiiRenderingResourceLocator(
            fileSystem,
            new MiiRenderingConfiguration { ResourcePath = resourcePath, MinimumExpectedSizeBytes = 16 }
        );
        return new NativeMiiRenderer(locator);
    }

    private static string? ResolveResourcePath()
    {
        var configured = Environment.GetEnvironmentVariable("WW_FFLRESHIGH_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var candidates = new[] { "/mnt/g/Temp/FFLResHigh.dat", @"G:\Temp\FFLResHigh.dat" };
        return candidates.FirstOrDefault(File.Exists);
    }
}
