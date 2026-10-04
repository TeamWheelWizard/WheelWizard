using System.Globalization;
using System.Text;

namespace WheelWizard.Settings.Types;

/// <summary>
/// A setting stored in the recomp's own <c>Config.toml</c>, which Wheel Wizard shares with the
/// in-game settings bar. Values are formatted exactly the way the runtime's own writer formats
/// them: booleans bare and lowercase, strings double-quoted, numbers invariant.
/// </summary>
public interface IRecompSetting
{
    string Name { get; }
    string Section { get; }
    string GetStringValue();
    bool SetFromString(string value, bool skipSave = false);
    void Reset(bool skipSave = false);
}

public class RecompSetting<T>((string Section, string Key) location, T defaultValue, Action<IRecompSetting> saveAction)
    : Setting<T>(location.Key, defaultValue),
        IRecompSetting
{
    public string Section { get; } = location.Section;

    protected override void ApplyValue(bool skipSave)
    {
        if (!skipSave)
            saveAction(this);
    }

    public new RecompSetting<T> SetValidation(Func<T, bool> validation)
    {
        base.SetValidation(validation);
        return this;
    }

    public string GetStringValue() =>
        Value switch
        {
            bool flag => flag ? "true" : "false",
            string text => QuoteBasicString(text),
            double number => number.ToString("0.0###", CultureInfo.InvariantCulture),
            _ => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    /// <summary>
    /// A TOML basic string must escape backslashes and quotes. Writing a Windows path raw makes the
    /// whole file invalid TOML, and the runtime then silently discards every setting in it.
    /// </summary>
    private static string QuoteBasicString(string text) => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>
    /// Reads a TOML literal from the file. An unparsable value returns <see langword="false"/> and
    /// keeps the current value, mirroring the runtime, which silently discards an invalid value and
    /// falls back to its default.
    /// </summary>
    public bool SetFromString(string tomlValue, bool skipSave = false)
    {
        var literal = tomlValue.Trim();
        return typeof(T) switch
        {
            { } t when t == typeof(string) => Set((T)(object)Unquote(literal), skipSave),
            { } t when t == typeof(bool) => bool.TryParse(literal, out var flag) && Set((T)(object)flag, skipSave),
            { } t when t == typeof(double) => double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number)
                && Set((T)(object)number, skipSave),
            _ => throw new InvalidOperationException($"Unsupported type: {typeof(T).Name}"),
        };
    }

    private static string Unquote(string literal)
    {
        if (literal.Length < 2)
            return literal;
        // A single-quoted TOML literal string has no escapes; a double-quoted basic string does, and
        // the backend writes paths with escaped backslashes, so they must be decoded here.
        if (literal[0] == '\'' && literal[^1] == '\'')
            return literal[1..^1];
        if (literal[0] != '"' || literal[^1] != '"')
            return literal;

        var inner = literal[1..^1];
        var result = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length && (inner[i + 1] == '\\' || inner[i + 1] == '"'))
                i++;
            result.Append(inner[i]);
        }
        return result.ToString();
    }
}
