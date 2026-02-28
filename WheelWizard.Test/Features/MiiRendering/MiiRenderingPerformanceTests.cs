using System.Diagnostics;
using Testably.Abstractions;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Utilities.Mockers;

namespace WheelWizard.Test.Features.MiiRendering;

public class MiiRenderingPerformanceTests
{
    [Fact]
    public void NativeRenderer_DragPreviewScale_ShouldBeAtLeast10xFaster_ThanFullQuality()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;

        var renderer = CreateRenderer(resourcePath);
        var mii = new MiiFactory().Create();
        var serialized = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serialized.IsSuccess, serialized.Error?.Message);

        var fullSpec = new MiiImageSpecifications
        {
            Size = MiiImageSpecifications.ImageSize.medium,
            Type = MiiImageSpecifications.BodyType.all_body,
            Expression = MiiImageSpecifications.FaceExpression.normal,
            BackgroundColor = "FFFFFF00",
            InstanceCount = 1,
            RenderScale = 1f,
        };

        var previewSpec = fullSpec.Clone();
        previewSpec.RenderScale = 0.05f;

        // Warm-up both paths so measurements focus on steady-state renderer cost.
        var warmupFull = renderer.RenderToBuffer(mii, serialized.Value, fullSpec);
        Assert.True(warmupFull.IsSuccess, warmupFull.Error?.Message);
        var warmupPreview = renderer.RenderToBuffer(mii, serialized.Value, previewSpec);
        Assert.True(warmupPreview.IsSuccess, warmupPreview.Error?.Message);

        const int fullIterations = 4;
        const int previewIterations = 8;
        var fullMs = MeasureAverageMs(renderer, mii, serialized.Value, fullSpec, fullIterations);
        var previewMs = MeasureAverageMs(renderer, mii, serialized.Value, previewSpec, previewIterations);
        var speedup = fullMs / Math.Max(previewMs, 0.001);

        Assert.True(speedup >= 10.0, $"Expected >=10x speedup. full={fullMs:F2}ms preview={previewMs:F2}ms speedup={speedup:F2}x");
    }

    private static double MeasureAverageMs(
        NativeMiiRenderer renderer,
        WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Mii mii,
        string studioData,
        MiiImageSpecifications baseSpec,
        int iterations
    )
    {
        var total = 0.0;
        for (var i = 0; i < iterations; i++)
        {
            var spec = baseSpec.Clone();
            spec.CharacterRotate = new(spec.CharacterRotate.X, i * 11, spec.CharacterRotate.Z);

            var sw = Stopwatch.StartNew();
            var result = renderer.RenderToBuffer(mii, studioData, spec);
            sw.Stop();

            Assert.True(result.IsSuccess, result.Error?.Message);
            total += sw.Elapsed.TotalMilliseconds;
        }

        return total / iterations;
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

        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "FFLResHigh.dat"),
            Path.Combine(Environment.CurrentDirectory, "Resources", "FFLResHigh.dat"),
            "/mnt/g/Temp/FFLResHigh.dat",
            @"G:\Temp\FFLResHigh.dat",
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
