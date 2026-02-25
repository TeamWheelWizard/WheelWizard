namespace WheelWizard.Settings;

public enum SettingsValidationCode
{
    InvalidUserFolderPath,
    InvalidDolphinLocation,
    InvalidGameLocation,
}

public sealed class SettingsValidationIssue(SettingsValidationCode code, string settingName, string message)
{
    public SettingsValidationCode Code { get; } = code;
    public string SettingName { get; } = settingName;
    public string Message { get; } = message;

    public override string ToString() => $"[{Code}] {SettingName}: {Message}";
}

public sealed class SettingsValidationReport(IReadOnlyList<SettingsValidationIssue> issues)
{
    public IReadOnlyList<SettingsValidationIssue> Issues { get; } = issues;
    public bool IsValid => Issues.Count == 0;

    public string ToSummaryText()
    {
        if (IsValid)
            return "All required settings are valid.";

        return string.Join("; ", Issues.Select(issue => issue.ToString()));
    }
}
