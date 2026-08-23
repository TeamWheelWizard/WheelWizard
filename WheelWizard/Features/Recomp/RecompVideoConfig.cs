using System.Globalization;

namespace WheelWizard.Recomp;

public static class RecompVideoConfig
{
    private const int WiiInternalWidth = 640;
    private const int WiiInternalHeight = 528;

    public static IReadOnlyList<double> ResolutionMultipliers { get; } = [1.0, 1.5, 2.0, 3.0];

    /// <summary>
    /// The graphics APIs WheelWizard offers, in the order they are shown. The backend enumerates more
    /// (auto, d3d11, opengl, ...), but only these two actually run the game reliably, so nothing else
    /// is ever offered. A value outside this list stays untouched until the user picks one of these.
    /// </summary>
    public static IReadOnlyList<string> OfferedGraphicsApis { get; } = ["d3d12", "vulkan"];

    /// <summary>The label for a graphics API value, for example <c>DirectX 12</c> for <c>d3d12</c>.</summary>
    public static string DescribeGraphicsApi(string api) =>
        api switch
        {
            "d3d12" => "DirectX 12",
            "vulkan" => "Vulkan",
            _ => api,
        };

    /// <summary>The label for a multiplier, for example <c>2.0x (1280x1056)</c>.</summary>
    public static string DescribeResolution(double multiplier)
    {
        var width = (int)Math.Round(WiiInternalWidth * multiplier);
        var height = (int)Math.Round(WiiInternalHeight * multiplier);
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0}x ({1}x{2})", multiplier, width, height);
    }
}
