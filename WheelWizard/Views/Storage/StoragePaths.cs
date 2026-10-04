using Avalonia.Platform.Storage;

namespace WheelWizard.Views.Storage;

public static class StoragePaths
{
    public static string? TryResolveLocalPath(IStorageItem? item)
    {
        if (item == null)
            return null;

        try
        {
            var localPath = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
                return localPath;
        }
        catch
        {
            // Some platforms might throw if local paths are unsupported; ignore and fall back to URI inspection.
        }

        var uri = item.Path;
        if (uri != null)
        {
            if (uri.IsAbsoluteUri && uri.IsFile)
            {
                try
                {
                    return uri.LocalPath;
                }
                catch (InvalidOperationException)
                {
                    // Ignore and fall through to raw string handling.
                }
            }

            if (uri.IsAbsoluteUri)
                return null;
            var raw = uri.ToString();
            if (!string.IsNullOrWhiteSpace(raw) && Path.IsPathRooted(raw))
                return raw;
        }

        return null;
    }
}
