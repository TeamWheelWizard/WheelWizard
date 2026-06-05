using System.Reflection;
using YamlDotNet.RepresentationModel;

namespace WheelWizard.Localization;

public sealed class EmbeddedYamlLocalizationService : ILocalizationService
{
    private const string DefaultLanguage = "en";
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _translations;
    private readonly object _languageLock = new();
    private string _currentLanguage = DefaultLanguage;

    public EmbeddedYamlLocalizationService()
        : this(typeof(EmbeddedYamlLocalizationService).Assembly) { }

    internal EmbeddedYamlLocalizationService(Assembly resourceAssembly)
    {
        _translations = LoadTranslations(resourceAssembly);
        if (_translations.Count == 0)
            throw new InvalidOperationException("No embedded YAML language files were found.");
    }

    public string CurrentLanguage
    {
        get
        {
            lock (_languageLock)
                return _currentLanguage;
        }
    }

    public IReadOnlyCollection<string> AvailableLanguages => _translations.Keys;

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = DefaultLanguage;

        var normalizedLanguage = NormalizeLanguage(languageCode);
        if (!HasLanguage(normalizedLanguage))
            normalizedLanguage = DefaultLanguage;

        lock (_languageLock)
            _currentLanguage = normalizedLanguage;
    }

    public string Translate(string key)
    {
        return TranslateForLanguage(key, CurrentLanguage);
    }

    public string TranslateForLanguage(string key, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var normalizedLanguage = NormalizeLanguage(languageCode);

        if (TryGetValue(normalizedLanguage, key, out var value))
            return value;

        if (
            !string.Equals(normalizedLanguage, DefaultLanguage, StringComparison.OrdinalIgnoreCase)
            && TryGetValue(DefaultLanguage, key, out value)
        )
            return value;

        return key;
    }

    public bool TryTranslateForLanguage(string key, string languageCode, out string value)
    {
        value = string.Empty;
        return !string.IsNullOrWhiteSpace(key) && TryGetValue(NormalizeLanguage(languageCode), key, out value);
    }

    public bool HasLanguage(string languageCode)
    {
        return _translations.ContainsKey(NormalizeLanguage(languageCode));
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadTranslations(Assembly resourceAssembly)
    {
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var resourceNames = resourceAssembly
            .GetManifestResourceNames()
            .Where(name =>
                name.Contains(".Resources.Languages.", StringComparison.Ordinal)
                && name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            );

        foreach (var resourceName in resourceNames)
        {
            using var stream = resourceAssembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                continue;

            using var reader = new StreamReader(stream);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                continue;

            foreach (var (languageNode, translationsNode) in root.Children)
            {
                if (languageNode is not YamlScalarNode languageScalar || translationsNode is not YamlMappingNode translationMap)
                    continue;

                var languageCode = NormalizeLanguage(languageScalar.Value ?? string.Empty);
                if (string.IsNullOrWhiteSpace(languageCode))
                    continue;

                var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                FlattenYamlNode(translationMap, null, flattened);
                translations[languageCode] = flattened;
            }
        }

        return translations;
    }

    private static void FlattenYamlNode(YamlNode node, string? prefix, Dictionary<string, string> values)
    {
        if (node is YamlMappingNode mapping)
        {
            foreach (var (childKey, childValue) in mapping.Children)
            {
                if (childKey is not YamlScalarNode keyScalar || string.IsNullOrWhiteSpace(keyScalar.Value))
                    continue;

                var key = prefix == null ? keyScalar.Value : $"{prefix}.{keyScalar.Value}";
                FlattenYamlNode(childValue, key, values);
            }

            return;
        }

        if (prefix == null)
            return;

        values[prefix] = node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : node.ToString();
    }

    private bool TryGetValue(string languageCode, string key, out string value)
    {
        value = string.Empty;
        return _translations.TryGetValue(languageCode, out var languageValues) && languageValues.TryGetValue(key, out value!);
    }

    private static string NormalizeLanguage(string languageCode)
    {
        var normalized = languageCode.Trim().Replace("_", "-", StringComparison.Ordinal);
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
            normalized = normalized[..dashIndex];

        return normalized.ToLowerInvariant();
    }
}
