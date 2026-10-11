using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Format;

namespace WheelWizard.MiiAnimations.Library;

public sealed class MiiAnimationLibrary(ILogger<MiiAnimationLibrary> logger) : IMiiAnimationLibrary
{
    private const string ResourcePrefix = "MiiAnimations/";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> ResourceNames = new(
        () =>
            typeof(MiiAnimationLibrary)
                .Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .ToDictionary(name => Normalize(name[ResourcePrefix.Length..]), name => name, StringComparer.OrdinalIgnoreCase)
    );

    private readonly ConcurrentDictionary<string, MiiAnimation?> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public MiiAnimation? Get(string path) => _loaded.GetOrAdd(Normalize(path), Load);

    public IReadOnlyList<string> List(string folder)
    {
        var prefix = Normalize(folder) + "/";
        return ResourceNames
            .Value.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !path[prefix.Length..].Contains('/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // RecursiveDir in resource names uses the build machine's separator.
    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private MiiAnimation? Load(string path)
    {
        if (!ResourceNames.Value.TryGetValue(path, out var resource))
        {
            logger.LogWarning("Mii animation {Path} doesn't exist", path);
            return null;
        }

        try
        {
            using var stream = typeof(MiiAnimationLibrary).Assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return MiiAnimFormat.Read(memory.ToArray());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Couldn't read Mii animation {Path}", path);
            return null;
        }
    }
}
