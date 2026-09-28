using System.Text.RegularExpressions;
using WheelWizard.Localization;
using Xunit.Abstractions;
using YamlDotNet.RepresentationModel;

namespace WheelWizard.Test.Features.Localization;

public class TranslationCatalogTests(ITestOutputHelper output)
{
    [Fact]
    public void ImportedCatalogHasValidStructureAndMatchingPlaceholders()
    {
        var assembly = typeof(EmbeddedYamlLocalizationService).Assembly;
        var languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.Contains(".Resources.Languages.") && n.EndsWith(".yml")))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            var yaml = new YamlStream();
            yaml.Load(reader);
            var root = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
            foreach (var (language, values) in root.Children)
            {
                var code = Assert.IsType<YamlScalarNode>(language).Value!;
                // Empty imported language stubs are intentionally unavailable in the application.
                if (values is YamlScalarNode { Value: null or "" })
                    continue;
                var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Flatten(Assert.IsType<YamlMappingNode>(values), "", flattened);
                Assert.True(languages.TryAdd(code, flattened), $"Duplicate language: {code}");
            }
        }
        Assert.True(languages.ContainsKey("en"), "English fallback must exist.");
        foreach (var language in LocalizationLanguageCatalog.SupportedLanguages)
            Assert.True(languages.ContainsKey(language.Code), $"Missing catalog: {language.Code}");
        var mismatches = new List<string>();
        foreach (var (language, values) in languages)
        {
            var missing = languages["en"].Keys.Except(values.Keys).ToArray();
            output.WriteLine($"{language}: {missing.Length} missing keys use English fallback.");
            foreach (var (key, value) in values)
            {
                if (languages["en"].TryGetValue(key, out var english) && !Placeholders(english).SetEquals(Placeholders(value)))
                    mismatches.Add($"{language}:{key}");
            }
            foreach (var key in values.Keys.Where(k => Regex.IsMatch(k, @"\.(zero|one|two|few|many)$")))
                Assert.True(values.ContainsKey(key[..key.LastIndexOf('.')] + ".other"), $"{language}:{key} needs an other fallback.");
        }
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches.Order()));
    }

    private static HashSet<string> Placeholders(string value) =>
        Regex.Matches(value, @"\{\$\d+\}").Select(match => match.Value).ToHashSet();

    private static void Flatten(YamlMappingNode map, string prefix, Dictionary<string, string> values)
    {
        foreach (var (key, value) in map.Children)
        {
            var segment = Assert.IsType<YamlScalarNode>(key).Value;
            Assert.False(string.IsNullOrWhiteSpace(segment));
            var path = string.IsNullOrEmpty(prefix) ? segment! : $"{prefix}.{segment}";
            if (value is YamlMappingNode children)
                Flatten(children, path, values);
            else
                Assert.True(values.TryAdd(path, Assert.IsType<YamlScalarNode>(value).Value ?? ""), $"Duplicate key: {path}");
        }
    }
}
