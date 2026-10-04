namespace WheelWizard.Shared.Processes;

public static class ShellQuoting
{
    public static string QuoteArgument(string value, bool isWindows) => isWindows ? $"\"{value}\"" : QuoteUnixArgument(value);

    public static string QuoteUnixArgument(string value) => $"'{value.Replace("'", "'\\''")}'";

    public static string QuotePowerShellArgument(string value) => $"'{value.Replace("'", "''")}'";
}
