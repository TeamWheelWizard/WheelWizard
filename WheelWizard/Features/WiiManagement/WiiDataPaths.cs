using System.IO.Abstractions;

namespace WheelWizard.WiiManagement;

public static class WiiDataPaths
{
    public static string GetMiiDbFilePath(this IPath path, string nandFolder) =>
        path.Combine(nandFolder, "shared2", "menu", "FaceLib", "RFL_DB.dat");

    public static string GetRrRatingFilePath(this IPath path, string nandFolder) =>
        path.Combine(nandFolder, "shared2", "Pulsar", "RetroRewind6", "RRRating.pul");
}
