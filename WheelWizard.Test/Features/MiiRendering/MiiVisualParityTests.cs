using System.Runtime.InteropServices;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Testably.Abstractions;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Services;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features.MiiRendering;

public class MiiVisualParityTests
{
    private static readonly HttpClient NintendoApiClient = new() { BaseAddress = new Uri("https://studio.mii.nintendo.com") };
    private const string ReferenceMiiBase64 =
        "wBAASAOzA8EDtQByACADtQB4AAAAAAAAgAAAAAAAAAAgF4+gmVMm1SCSjpgAbWAvAIoAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void NativeRenderer_ShouldMatchReferenceBaseline_WhenBaselineIsProvided()
    {
        var strictParity = IsStrictParityEnabled();
        var baselineDirectory = Environment.GetEnvironmentVariable("WW_MII_PARITY_BASELINE_DIR");
        if (string.IsNullOrWhiteSpace(baselineDirectory) || !Directory.Exists(baselineDirectory))
        {
            if (strictParity)
                Assert.Fail("WW_MII_PARITY_BASELINE_DIR is required when WW_REQUIRE_MII_PARITY is enabled.");
            return;
        }

        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
        {
            if (strictParity)
                Assert.Fail("Strict parity is enabled, but FFL resources could not be resolved.");
            return;
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), "WheelWizard", "MiiParity");
        Directory.CreateDirectory(outputDirectory);

        var renderer = CreateRenderer(resourcePath);
        var mii = CreateReferenceMii();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var scenarios = new Dictionary<string, MiiImageSpecifications>
        {
            ["face_normal_270"] = new()
            {
                Size = MiiImageSpecifications.ImageSize.small,
                Type = MiiImageSpecifications.BodyType.face,
                Expression = MiiImageSpecifications.FaceExpression.normal,
                BackgroundColor = "FFFFFF00",
                InstanceCount = 1,
            },
            ["body_smile_270"] = new()
            {
                Size = MiiImageSpecifications.ImageSize.small,
                Type = MiiImageSpecifications.BodyType.all_body,
                Expression = MiiImageSpecifications.FaceExpression.smile,
                BackgroundColor = "FFFFFF00",
                InstanceCount = 1,
            },
        };
        var parityFailures = new List<string>();

        foreach (var scenario in scenarios)
        {
            var renderResult = renderer.RenderToBuffer(mii, serializedResult.Value, scenario.Value);
            Assert.True(renderResult.IsSuccess, renderResult.Error?.Message);

            var outputPath = Path.Combine(outputDirectory, scenario.Key + ".png");
            SaveBgraAsPng(renderResult.Value.BgraPixels, renderResult.Value.Width, renderResult.Value.Height, outputPath);

            var baselinePath = Path.Combine(baselineDirectory, scenario.Key + ".png");
            if (!File.Exists(baselinePath))
            {
                if (strictParity)
                    Assert.Fail($"Missing baseline: {baselinePath}");
                continue;
            }

            var (baselinePixels, baselineWidth, baselineHeight) = LoadPngRgbaPixels(baselinePath);
            var nativePixels = ConvertBgraToRgba(renderResult.Value.BgraPixels);
            var overlap = CalculateExactOverlap(
                nativePixels,
                renderResult.Value.Width,
                renderResult.Value.Height,
                baselinePixels,
                baselineWidth,
                baselineHeight
            );

            Assert.True(
                overlap >= 1.0,
                $"Parity mismatch for {scenario.Key}: overlap={overlap:P3}, output={outputPath}, baseline={baselinePath}"
            );
        }
    }

    [Fact]
    public async Task NativeRenderer_ShouldMatchNintendoApi_WhenApiParityIsEnabled()
    {
        var enabled = IsApiParityEnabled();
        var strictParity = IsStrictApiParityEnabled();
        if (!enabled)
            return;

        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
        {
            Assert.Fail("API parity is enabled, but FFL resources could not be resolved.");
        }

        var outputDirectory = Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            "artifacts",
            "mii-api-parity"
        );
        Directory.CreateDirectory(outputDirectory);
        var summaryPath = Path.Combine(outputDirectory, "parity-summary.txt");
        await File.WriteAllTextAsync(summaryPath, $"resource={resourcePath}{Environment.NewLine}");

        var renderer = CreateRenderer(resourcePath);
        var mii = CreateReferenceMii();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var scenarios = new Dictionary<string, MiiImageSpecifications>
        {
            ["api_face_normal_270"] = new()
            {
                Size = MiiImageSpecifications.ImageSize.small,
                Type = MiiImageSpecifications.BodyType.face,
                Expression = MiiImageSpecifications.FaceExpression.normal,
                BackgroundColor = "FFFFFF00",
                InstanceCount = 1,
            },
            ["api_body_normal_270"] = new()
            {
                Size = MiiImageSpecifications.ImageSize.small,
                Type = MiiImageSpecifications.BodyType.all_body,
                Expression = MiiImageSpecifications.FaceExpression.normal,
                BackgroundColor = "FFFFFF00",
                InstanceCount = 1,
            },
        };
        var parityFailures = new List<string>();

        foreach (var scenario in scenarios)
        {
            await File.AppendAllTextAsync(summaryPath, $"{scenario.Key}: native_render_start{Environment.NewLine}");
            var nativeRender = renderer.RenderToBuffer(mii, serializedResult.Value, scenario.Value);
            Assert.True(nativeRender.IsSuccess, nativeRender.Error?.Message);
            await File.AppendAllTextAsync(summaryPath, $"{scenario.Key}: native_render_ok{Environment.NewLine}");

            var nativePath = Path.Combine(outputDirectory, scenario.Key + ".native.png");
            SaveBgraAsPng(nativeRender.Value.BgraPixels, nativeRender.Value.Width, nativeRender.Value.Height, nativePath);
            await File.AppendAllTextAsync(summaryPath, $"{scenario.Key}: native_save_ok{Environment.NewLine}");

            var apiPath = Path.Combine(outputDirectory, scenario.Key + ".api.png");
            await using (
                var response = await NintendoApiClient.GetStreamAsync(BuildNintendoApiRequest(serializedResult.Value, scenario.Value))
            )
            await using (var apiFile = File.Create(apiPath))
            {
                await response.CopyToAsync(apiFile);
            }
            await File.AppendAllTextAsync(summaryPath, $"{scenario.Key}: api_download_ok{Environment.NewLine}");

            var (apiPixels, apiWidth, apiHeight) = LoadPngRgbaPixels(apiPath);
            await File.AppendAllTextAsync(summaryPath, $"{scenario.Key}: api_decode_ok{Environment.NewLine}");

            var nativePixels = ConvertBgraToRgba(nativeRender.Value.BgraPixels);
            var overlap = CalculateExactOverlap(
                nativePixels,
                nativeRender.Value.Width,
                nativeRender.Value.Height,
                apiPixels,
                apiWidth,
                apiHeight
            );
            await File.AppendAllTextAsync(summaryPath, $"{scenario.Key}: overlap={overlap:F6}{Environment.NewLine}");

            if (strictParity || enabled)
            {
                if (overlap < 1.0)
                {
                    parityFailures.Add(
                        $"Nintendo API parity mismatch for {scenario.Key}: overlap={overlap:P3}, native={nativePath}, api={apiPath}"
                    );
                }
            }
        }

        if (parityFailures.Count > 0)
            Assert.Fail(string.Join(Environment.NewLine, parityFailures));
    }

    [Fact]
    public void NativeRenderer_ShouldRenderSmoke_InVisualParitySuite()
    {
        var resourcePath = ResolveResourcePath();
        if (resourcePath == null)
            return;

        var renderer = CreateRenderer(resourcePath);
        var mii = CreateReferenceMii();
        var serializedResult = MiiStudioDataSerializer.Serialize(mii);
        Assert.True(serializedResult.IsSuccess, serializedResult.Error?.Message);

        var result = renderer.RenderToBuffer(
            mii,
            serializedResult.Value,
            new MiiImageSpecifications
            {
                Size = MiiImageSpecifications.ImageSize.small,
                Type = MiiImageSpecifications.BodyType.face,
                Expression = MiiImageSpecifications.FaceExpression.normal,
                BackgroundColor = "FFFFFF00",
                InstanceCount = 1,
            }
        );

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotEmpty(result.Value.BgraPixels);
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

    private static bool IsStrictParityEnabled()
    {
        var value = Environment.GetEnvironmentVariable("WW_REQUIRE_MII_PARITY");
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApiParityEnabled()
    {
        var value = Environment.GetEnvironmentVariable("WW_COMPARE_NINTENDO_API");
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrictApiParityEnabled()
    {
        var value = Environment.GetEnvironmentVariable("WW_REQUIRE_MII_API_PARITY");
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
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

    private static string BuildNintendoApiRequest(string studioData, MiiImageSpecifications specifications)
    {
        var queryParts = new Dictionary<string, string>
        {
            ["data"] = studioData,
            ["type"] = specifications.Type.ToString(),
            ["expression"] = specifications.Expression.ToString(),
            ["width"] = ((int)specifications.Size).ToString(),
            ["characterXRotate"] = ((int)MathF.Round(specifications.CharacterRotate.X)).ToString(),
            ["characterYRotate"] = ((int)MathF.Round(specifications.CharacterRotate.Y)).ToString(),
            ["characterZRotate"] = ((int)MathF.Round(specifications.CharacterRotate.Z)).ToString(),
            ["bgColor"] = string.IsNullOrWhiteSpace(specifications.BackgroundColor) ? "FFFFFF00" : specifications.BackgroundColor,
            ["instanceCount"] = specifications.InstanceCount.ToString(),
            ["cameraXRotate"] = ((int)MathF.Round(specifications.CameraRotate.X)).ToString(),
            ["cameraYRotate"] = ((int)MathF.Round(specifications.CameraRotate.Y)).ToString(),
            ["cameraZRotate"] = ((int)MathF.Round(specifications.CameraRotate.Z)).ToString(),
        };

        var builder = new StringBuilder("/miis/image.png?");
        var first = true;
        foreach (var pair in queryParts)
        {
            if (!first)
                builder.Append('&');
            builder.Append(pair.Key).Append('=').Append(Uri.EscapeDataString(pair.Value));
            first = false;
        }

        return builder.ToString();
    }

    private static void SaveBgraAsPng(byte[] bgraPixels, int width, int height, string outputPath)
    {
        using var image = new Image<Rgba32>(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                image[x, y] = new Rgba32(bgraPixels[i + 2], bgraPixels[i + 1], bgraPixels[i + 0], bgraPixels[i + 3]);
            }
        }

        image.SaveAsPng(outputPath);
    }

    private static (byte[] Pixels, int Width, int Height) LoadPngRgbaPixels(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        var rgba = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(rgba);

        var bytes = new byte[rgba.Length * 4];
        MemoryMarshal.AsBytes(rgba.AsSpan()).CopyTo(bytes);
        return (bytes, image.Width, image.Height);
    }

    private static byte[] ConvertBgraToRgba(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            rgba[i + 0] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i + 0];
            rgba[i + 3] = bgra[i + 3];
        }

        return rgba;
    }

    private static double CalculateExactOverlap(
        byte[] leftPixels,
        int leftWidth,
        int leftHeight,
        byte[] rightPixels,
        int rightWidth,
        int rightHeight
    )
    {
        if (leftWidth != rightWidth || leftHeight != rightHeight)
            return 0;

        var matchingPixels = 0;
        var totalPixels = leftWidth * leftHeight;
        for (var i = 0; i < leftPixels.Length; i += 4)
        {
            if (
                leftPixels[i] == rightPixels[i]
                && leftPixels[i + 1] == rightPixels[i + 1]
                && leftPixels[i + 2] == rightPixels[i + 2]
                && leftPixels[i + 3] == rightPixels[i + 3]
            )
            {
                matchingPixels++;
            }
        }

        return matchingPixels / (double)totalPixels;
    }

    private static Mii CreateReferenceMii()
    {
        var bytes = Convert.FromBase64String(ReferenceMiiBase64);
        var deserialized = MiiSerializer.Deserialize(bytes);
        Assert.True(deserialized.IsSuccess, deserialized.Error?.Message);
        return deserialized.Value;
    }
}
